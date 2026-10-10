using Microsoft.AspNetCore.Identity;

namespace KobiFabrika.Infrastructure.Identity;

/// <summary>Uygulama kullanıcısı. Telefon Identity'nin PhoneNumber alanında (+90 ile E.164) tutulur.</summary>
public class AppUser : IdentityUser<Guid>
{
    public AppUser() => Id = Guid.CreateVersion7();

    public required string FullName { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Girişte açılacak işletme: en son seçilen. Üyelik pasifse başka aktif üyeliğe düşülür.</summary>
    public Guid? LastBusinessId { get; set; }
}
