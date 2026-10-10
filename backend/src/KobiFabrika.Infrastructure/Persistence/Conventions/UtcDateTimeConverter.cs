using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace KobiFabrika.Infrastructure.Persistence.Conventions;

/// <summary>
/// Veritabanında tüm zamanlar UTC. Yerel saat (Kind=Local) yazılmaya çalışılırsa hata verir;
/// okunan değerler Kind=Utc olarak işaretlenir.
/// </summary>
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => EnsureUtc(v),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
{
    public static DateTime EnsureUtc(DateTime value) => value.Kind == DateTimeKind.Local
        ? throw new InvalidOperationException("Veritabanına yerel saat yazılamaz; DateTime.UtcNow veya UTC değer kullanın.")
        : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
