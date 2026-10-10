using System.Globalization;
using System.Text.Json;
using FluentValidation;
using KobiFabrika.Domain.Common;
using Microsoft.AspNetCore.Identity;

namespace KobiFabrika.Api.Common;

/// <summary>
/// FluentValidation'ı minimal API'ye bağlar: <c>.Validate&lt;T&gt;()</c> eklenen uçta istek gövdesi doğrulanır,
/// hata varsa Türkçe mesajlı ValidationProblem (400, <c>errors</c> alan adları camelCase) döner.
/// </summary>
public static class Validation
{
    public static void ConfigureDefaults()
    {
        ValidatorOptions.Global.LanguageManager.Culture = new CultureInfo("tr");
        ValidatorOptions.Global.PropertyNameResolver = (_, member, _) =>
            member is null ? null : JsonNamingPolicy.CamelCase.ConvertName(member.Name);
    }

    public static RouteHandlerBuilder Validate<T>(this RouteHandlerBuilder builder) where T : class =>
        builder.AddEndpointFilter<ValidationFilter<T>>().ProducesValidationProblem();

    /// <summary>Identity hatalarını ilgili form alanına bağlar (şifre, e-posta).</summary>
    public static IResult IdentityProblem(IdentityResult result)
    {
        var errors = result.Errors
            .GroupBy(e => e.Code switch
            {
                _ when e.Code.StartsWith("Password", StringComparison.Ordinal) && e.Code != "PasswordMismatch" => "password",
                "PasswordMismatch" => "currentPassword",
                "DuplicateEmail" or "DuplicateUserName" or "InvalidEmail" or "InvalidUserName" => "email",
                "InvalidToken" => "token",
                _ => "",
            })
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).Distinct().ToArray());
        return TypedResults.ValidationProblem(errors);
    }

    public static IResult FieldProblem(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    // Ortak kurallar (frontend src/lib/validation ile aynı).

    public static IRuleBuilderOptions<T, string> ValidPassword<T>(this IRuleBuilder<T, string> rule) => rule
        .NotEmpty().WithMessage("Şifre girin.")
        .MinimumLength(8).WithMessage("Şifre en az 8 karakter olmalı.")
        .MaximumLength(128).WithMessage("Şifre en fazla 128 karakter olabilir.")
        .Must(p => p.Any(char.IsLetter)).WithMessage("Şifre en az bir harf içermeli.")
        .Must(p => p.Any(char.IsAsciiDigit)).WithMessage("Şifre en az bir rakam içermeli.");

    public static IRuleBuilderOptions<T, string> ValidEmail<T>(this IRuleBuilder<T, string> rule) => rule
        .NotEmpty().WithMessage("E-posta adresi girin.")
        .MaximumLength(256).WithMessage("E-posta adresi çok uzun.")
        .EmailAddress().WithMessage("Geçerli bir e-posta adresi girin.");

    public static IRuleBuilderOptions<T, string> ValidFullName<T>(this IRuleBuilder<T, string> rule) => rule
        .Must(n => !string.IsNullOrWhiteSpace(n) && n.Trim().Length >= 2).WithMessage("Ad soyad girin.")
        .MaximumLength(200).WithMessage("Ad soyad en fazla 200 karakter olabilir.");

    public static IRuleBuilderOptions<T, string?> ValidPhone<T>(this IRuleBuilder<T, string?> rule) => rule
        .Must(p => PhoneNumber.TryNormalize(p, out _)).WithMessage(PhoneNumber.InvalidMessage);

    /// <summary>Para tutarı: boş geçilemez, 0'dan büyük, en fazla 2 ondalık (<see cref="Money"/>).</summary>
    public static IRuleBuilderOptions<T, decimal?> ValidMoney<T>(this IRuleBuilder<T, decimal?> rule) => rule
        .Must(v => Money.Validate(v) is null).WithMessage((_, v) => Money.Validate(v)!);

    public static IRuleBuilderOptions<T, string?> ValidIban<T>(this IRuleBuilder<T, string?> rule) => rule
        .Must(i => Iban.TryNormalize(i, out _)).WithMessage(Iban.InvalidMessage);

    /// <summary>Doğrulamadan geçmiş telefonu E.164'e çevirir.</summary>
    public static string NormalizePhone(string phone) =>
        PhoneNumber.TryNormalize(phone, out var e164) ? e164 : throw new ArgumentException(PhoneNumber.InvalidMessage);

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}

internal sealed class ValidationFilter<T>(IValidator<T> validator) : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (context.Arguments.OfType<T>().FirstOrDefault() is { } request)
        {
            var result = await validator.ValidateAsync(request, context.HttpContext.RequestAborted);
            if (!result.IsValid)
            {
                return TypedResults.ValidationProblem(result.ToDictionary());
            }
        }

        return await next(context);
    }
}
