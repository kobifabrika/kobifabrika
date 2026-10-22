using System.Globalization;
using KobiFabrika.Domain.Common;
using KobiFabrika.Infrastructure.Tenancy;

namespace KobiFabrika.Api.Features.Finance;

/// <summary>Müşteriye gönderilen linklerin ortak yardımcıları (WhatsApp, para biçimi, kiracı scope'u).</summary>
public static class ShareLinks
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>wa.me linki: telefon varsa o kişiye, yoksa kişi seçtiren genel link. Mesaj hazır gelir.</summary>
    public static string WhatsApp(string? e164Phone, string message)
    {
        var number = e164Phone?.TrimStart('+') ?? "";
        return $"https://wa.me/{number}?text={Uri.EscapeDataString(message)}";
    }

    public static string StatementMessage(string contactName, string businessName, string link) =>
        $"Merhaba {contactName}, {businessName} cari hesap ekstreniz: {link}";

    public static string PaymentMessage(string contactName, string businessName, decimal amount, string link) =>
        $"Merhaba {contactName}, {businessName} için {Money.Format(amount)} ödeme bilgileri: {link}";

    public static string QuoteMessage(string contactName, string businessName, string quoteNo, decimal total, string link) =>
        $"Merhaba {contactName}, {businessName} teklifi ({quoteNo}, {Money.Format(total)}): {link}";

    public static string Range(DateOnly? from, DateOnly to) => from is { } f
        ? $"{f.ToString("dd.MM.yyyy", Tr)} – {to.ToString("dd.MM.yyyy", Tr)}"
        : $"{to.ToString("dd.MM.yyyy", Tr)} tarihine kadarki";

    /// <summary>Herkese açık uçlar için: token'dan çözülen işletmenin kiracı bağlamında yeni bir DI scope'u.</summary>
    public static AsyncServiceScope TenantScope(this IServiceScopeFactory scopes, Guid businessId)
    {
        var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(businessId);
        return scope;
    }
}
