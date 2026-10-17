using KobiFabrika.Domain.Billing;
using KobiFabrika.Infrastructure.Billing;

namespace KobiFabrika.Api.Common;

/// <summary>
/// Pakete bağlı özellik kapısı (karar 2026-10-17): sadece yazma uçlarına takılır; okuma açık kalır, kapalı özellik
/// salt okunur görünür. Kapalıysa 403 <c>plan_feature</c> ("Kampanyalar Kalfa paketiyle açılır…").
/// </summary>
public static class PlanFeatureFilter
{
    public static RouteHandlerBuilder RequireFeature(this RouteHandlerBuilder builder, PlanFeatures feature) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var plans = context.HttpContext.RequestServices.GetRequiredService<IPlanAccess>();
            if (await plans.FeatureBlockedMessageAsync(feature, context.HttpContext.RequestAborted) is { } message)
            {
                return Problems.PlanFeature(message);
            }

            return await next(context);
        }).ProducesProblem(StatusCodes.Status403Forbidden);
}

/// <summary>
/// SMS kapalıyken (Messaging:SmsEnabled=false) SMS gönderen yazma uçları 409 <c>sms_disabled</c>. Pilot dönemde kampanyalar ve
/// otomasyonlar SMS'siz; e-posta ve WhatsApp kanalları sonraki adımda (docs/05).
/// </summary>
public static class SmsFeatureFilter
{
    public const string Message = "SMS gönderimi şu an kapalı. E-posta ve WhatsApp ile kampanya yakında açılacak.";

    public static RouteHandlerBuilder RequireSms(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var messaging = context.HttpContext.RequestServices
                .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Infrastructure.Messaging.MessagingOptions>>();
            return messaging.CurrentValue.SmsEnabled ? await next(context) : Problems.Conflict("sms_disabled", Message);
        });
}
