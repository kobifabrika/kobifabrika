namespace KobiFabrika.Infrastructure.Email;

/// <summary>"Email" bölümü. Geliştirmede Mailpit: localhost:1025, kimlik doğrulama ve TLS yok.</summary>
public sealed class EmailOptions
{
    public const string Section = "Email";

    public string FromName { get; set; } = "KobiFabrika";
    public string FromAddress { get; set; } = "bildirim@kobifabrika.com";
    public SmtpOptions Smtp { get; set; } = new();

    /// <summary>
    /// Queue: kuyruğa yazılır, Hangfire işi gönderir (canlı, geliştirme, E2E). Inline: kuyruğa yazılır ve hemen aynı istekte
    /// gönderilir (Hangfire'ın kapalı olduğu entegrasyon testleri).
    /// </summary>
    public string Delivery { get; set; } = "Queue";

    public bool IsInline => string.Equals(Delivery, "Inline", StringComparison.OrdinalIgnoreCase);

    public sealed class SmtpOptions
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 1025;
        public bool UseSsl { get; set; }
        public string? UserName { get; set; }
        public string? Password { get; set; }
    }
}
