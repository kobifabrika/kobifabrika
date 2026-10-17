namespace KobiFabrika.Infrastructure.Billing;

/// <summary>Abonelik ödemesi başlatma isteği (iyzico; şirket kurulunca).</summary>
public sealed record PaymentCheckoutRequest(Guid BusinessId, string PlanCode, string Period, decimal Amount, string BuyerEmail, string BuyerName);

public sealed record PaymentCheckout(string ProviderRef, string RedirectUrl);

/// <summary>
/// Online ödeme sağlayıcısı (iyzico abonelik). Şirket kurulana kadar kapalı (karar 2026-10-17): <see cref="IsEnabled"/> false,
/// arayüz "Online ödeme yakında; paket değişikliği için destekle iletişime geçin" gösterir. Gerçek uygulama "Faz 1 sonu".
/// </summary>
public interface IPaymentProvider
{
    bool IsEnabled { get; }

    Task<PaymentCheckout> StartCheckoutAsync(PaymentCheckoutRequest request, CancellationToken ct = default);
}

/// <summary>Sahte sağlayıcı: kapalıdır; çağrılırsa hata verir (testlerde ve geliştirmede ödeme akışı yok).</summary>
internal sealed class DisabledPaymentProvider : IPaymentProvider
{
    public bool IsEnabled => false;

    public Task<PaymentCheckout> StartCheckoutAsync(PaymentCheckoutRequest request, CancellationToken ct = default) =>
        throw new InvalidOperationException("Online ödeme henüz açık değil.");
}
