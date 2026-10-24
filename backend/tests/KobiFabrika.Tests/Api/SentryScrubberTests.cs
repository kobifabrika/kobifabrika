using KobiFabrika.Api.Common;
using Sentry;

namespace KobiFabrika.Tests.Api;

/// <summary>Sentry'ye kişisel veri gitmez: e-posta ve telefon maskelenir, kullanıcı (IP dahil) boşaltılır.</summary>
public sealed class SentryScrubberTests
{
    [Theory]
    [InlineData("Gönderilemedi: ayse.yilmaz@ornek.com.tr", "Gönderilemedi: ***@ornek.com.tr")]
    [InlineData("tel +905321234567 hatalı", "tel [telefon **67] hatalı")]
    [InlineData("tel +90 532 123 45 67", "tel [telefon **67]")]
    [InlineData("0532 123 4567 aranacak", "[telefon **67] aranacak")]
    [InlineData("5321234567", "[telefon **67]")]
    [InlineData("+4915123456789", "[telefon **89]")]
    [InlineData("Sipariş 2026-10-24 tutar 1250,50", "Sipariş 2026-10-24 tutar 1250,50")]
    [InlineData("id 3f2b8c1e-0000-4000-8000-000000000000", "id 3f2b8c1e-0000-4000-8000-000000000000")]
    public void Masks_emails_and_phones(string input, string expected) =>
        SentryScrubber.Mask(input).ShouldBe(expected);

    [Fact]
    public void Scrubs_event_message_exception_extra_query_and_user()
    {
        var sentryEvent = new SentryEvent(new InvalidOperationException("x"))
        {
            Message = new SentryMessage { Message = "Kayıt: {Email}", Formatted = "Kayıt: ali@ornek.com" },
            User = new SentryUser { Email = "ali@ornek.com", IpAddress = "31.223.77.108" },
        };
        sentryEvent.SentryExceptions = [new Sentry.Protocol.SentryException { Value = "05321234567 bulunamadı" }];
        sentryEvent.SetExtra("To", "veli@ornek.com");
        sentryEvent.Request.QueryString = "?email=ali@ornek.com";

        var scrubbed = SentryScrubber.Scrub(sentryEvent);

        scrubbed.Message!.Formatted.ShouldBe("Kayıt: ***@ornek.com");
        scrubbed.SentryExceptions!.Single().Value.ShouldBe("[telefon **67] bulunamadı");
        scrubbed.Extra["To"].ShouldBe("***@ornek.com");
        scrubbed.Request.QueryString.ShouldBe("?email=***@ornek.com");
        scrubbed.User.Email.ShouldBeNull();
        scrubbed.User.IpAddress.ShouldBeNull();
    }

    [Fact]
    public void Scrubs_breadcrumb_message_and_data()
    {
        var breadcrumb = new Breadcrumb("Giriş: ali@ornek.com", "default", new Dictionary<string, string> { ["tel"] = "+905321234567" });

        var scrubbed = SentryScrubber.Scrub(breadcrumb);

        scrubbed.Message.ShouldBe("Giriş: ***@ornek.com");
        scrubbed.Data!["tel"].ShouldBe("[telefon **67]");
    }
}
