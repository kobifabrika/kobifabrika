namespace KobiFabrika.Infrastructure.Email;

/// <summary>"Email" bölümü. Geliştirmede Mailpit: localhost:1025, kimlik doğrulama ve TLS yok.</summary>
public sealed class EmailOptions
{
    public const string Section = "Email";

    public string FromName { get; set; } = "KobiFabrika";
    public string FromAddress { get; set; } = "bildirim@kobifabrika.com";
    public SmtpOptions Smtp { get; set; } = new();

    public sealed class SmtpOptions
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 1025;
        public bool UseSsl { get; set; }
        public string? UserName { get; set; }
        public string? Password { get; set; }
    }
}
