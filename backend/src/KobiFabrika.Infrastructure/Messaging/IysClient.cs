using KobiFabrika.Domain.Contacts;
using Microsoft.Extensions.Logging;

namespace KobiFabrika.Infrastructure.Messaging;

/// <summary>Bir iznin İYS'ye bildirimi (ticari elektronik ileti onayı/reddi).</summary>
public sealed record IysConsent(string BrandCode, string Phone, ConsentChannel Channel, bool Granted, ConsentSource Source, DateTime At);

/// <summary>İYS'nin bir numara için kayıtlı durumu.</summary>
public sealed record IysConsentState(bool Granted, DateTime At);

public enum IysResult
{
    Accepted,
    Rejected,
}

/// <summary>
/// İleti Yönetim Sistemi (İYS). Gerçek bağlantı (izinleri bildirme ve sorgulama) gerçek SMS sağlayıcısıyla pilot öncesi yapılır;
/// şimdilik sahte uygulama. İzin değişiklikleri ContactConsents'e Pending yazılır, <c>iys-sync</c> işi buradan bildirir.
/// </summary>
public interface IIysClient
{
    string Name { get; }

    Task<IysResult> ReportConsentAsync(IysConsent consent, CancellationToken ct);

    /// <summary>Numaranın İYS'deki güncel durumu; kayıt yoksa null.</summary>
    Task<IysConsentState?> QueryConsentAsync(string brandCode, string phone, ConsentChannel channel, CancellationToken ct);
}

/// <summary>Sahte İYS: her bildirimi kabul eder, sorguda kayıt döndürmez.</summary>
internal sealed class FakeIysClient(ILogger<FakeIysClient> logger) : IIysClient
{
    public string Name => "fake";

    public Task<IysResult> ReportConsentAsync(IysConsent consent, CancellationToken ct)
    {
        logger.LogInformation("[İYS] {Brand} {Phone} {Channel} {Granted}", consent.BrandCode, consent.Phone, consent.Channel, consent.Granted);
        return Task.FromResult(IysResult.Accepted);
    }

    public Task<IysConsentState?> QueryConsentAsync(string brandCode, string phone, ConsentChannel channel, CancellationToken ct) =>
        Task.FromResult<IysConsentState?>(null);
}
