using KobiFabrika.Infrastructure.Businesses;
using KobiFabrika.Infrastructure.Email;
using KobiFabrika.Infrastructure.Identity;
using KobiFabrika.Infrastructure.Locations;
using KobiFabrika.Infrastructure.Storage;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace KobiFabrika.Tests.Infrastructure;

public sealed class InfrastructureServiceTests
{
    [Theory]
    [InlineData("sifre1234", true)]
    [InlineData("şifre1234", true)]
    [InlineData("12345678", false)]
    public async Task Password_requires_a_letter(string password, bool valid)
    {
        var result = await new LetterPasswordValidator().ValidateAsync(null!, new AppUser { FullName = "x" }, password);

        result.Succeeded.ShouldBe(valid);
        if (!valid)
        {
            result.Errors.Single().Description.ShouldBe("Şifre en az bir harf içermeli.");
        }
    }

    [Fact]
    public void Location_catalog_has_81_cities_and_their_districts()
    {
        var catalog = new LocationCatalog();

        catalog.Cities.Count.ShouldBe(81);
        catalog.Cities.Sum(c => c.Districts.Count).ShouldBe(973);
        catalog.IsValid("İstanbul", "Kadıköy").ShouldBeTrue();
        catalog.IsValid("İzmir", "Kadıköy").ShouldBeFalse();
        catalog.IsValid("Istanbul", "Kadıköy").ShouldBeFalse();
        catalog.IsValid("Ankara", null).ShouldBeFalse();
    }

    [Fact]
    public void Email_templates_encode_html_and_include_link()
    {
        var message = EmailTemplates.Invitation(
            "a@b.com", "<Şık> Kuaför", "Emir Kaya", "Personel", "http://localhost:3000/davet/abc?x=1&y=2");

        message.Subject.ShouldBe("<Şık> Kuaför seni KobiFabrika'ya davet etti");
        message.HtmlBody.ShouldContain("&lt;Şık&gt; Kuaför");
        message.HtmlBody.ShouldNotContain("<Şık>");
        message.HtmlBody.ShouldContain("http://localhost:3000/davet/abc?x=1&amp;y=2");
        message.TextBody.ShouldContain("http://localhost:3000/davet/abc?x=1&y=2");
    }

    [Fact]
    public void Invitation_token_hash_is_deterministic_and_token_is_random()
    {
        var (token1, hash1) = InvitationTokens.Create();
        var (token2, _) = InvitationTokens.Create();

        token1.ShouldNotBe(token2);
        hash1.Length.ShouldBe(32);
        InvitationTokens.Hash(token1).ShouldBe(hash1);
    }

    [Fact]
    public async Task Local_storage_saves_and_deletes_and_rejects_path_traversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "kf-storage-" + Guid.NewGuid().ToString("N"));
        var storage = new LocalFileStorage(Options.Create(new StorageOptions { LocalPath = root }), new FakeHostEnvironment());

        var url = await storage.SaveAsync("logos/abc", "logo.png", new MemoryStream([1, 2, 3]), TestContext.Current.CancellationToken);

        url.ShouldBe("/files/logos/abc/logo.png");
        File.Exists(Path.Combine(root, "logos", "abc", "logo.png")).ShouldBeTrue();

        await storage.DeleteAsync(url, TestContext.Current.CancellationToken);
        File.Exists(Path.Combine(root, "logos", "abc", "logo.png")).ShouldBeFalse();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            storage.SaveAsync("../disari", "x.png", new MemoryStream([1]), TestContext.Current.CancellationToken));

        Directory.Delete(root, recursive: true);
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
