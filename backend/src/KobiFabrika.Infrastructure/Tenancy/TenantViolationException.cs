namespace KobiFabrika.Infrastructure.Tenancy;

/// <summary>Bir işletmenin başka bir işletmenin verisine dokunma girişimi. API'de 403'e çevrilir.</summary>
public sealed class TenantViolationException(string message) : Exception(message);
