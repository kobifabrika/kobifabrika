using KobiFabrika.Domain.Businesses;
using Microsoft.AspNetCore.Authorization;

namespace KobiFabrika.Api.Common.Authorization;

/// <summary>
/// Yetki policy'leri. Roller: Owner (Sahip), Manager (Yönetici), Staff (Personel), Accountant (Mali Müşavir, salt okunur).
/// Frontend aynı matrisi src/lib/permissions.ts'te tutar (sadece görünürlük için).
/// </summary>
public static class Policies
{
    /// <summary>Aktif işletmenin üyesi ve kurulum bitmiş (her rol; Accountant dahil okuma).</summary>
    public const string Panel = nameof(Panel);

    /// <summary>Kayıt yazabilenler: Owner, Manager, Staff.</summary>
    public const string CanWrite = nameof(CanWrite);

    /// <summary>İşletme ayarlarını görme: Owner, Manager, Accountant (salt okunur). Staff göremez.</summary>
    public const string ViewSettings = nameof(ViewSettings);

    /// <summary>İşletme ayarlarını değiştirme: Owner, Manager.</summary>
    public const string ManageBusiness = nameof(ManageBusiness);

    /// <summary>Ekip listesi ve yönetimi: Owner, Manager (Owner'a dokunma kuralı handler'da).</summary>
    public const string ManageTeam = nameof(ManageTeam);

    /// <summary>Aktivite günlüğü: Owner, Manager, Accountant.</summary>
    public const string ViewAuditLog = nameof(ViewAuditLog);

    /// <summary>Müşteri/tedarikçi silme ve arşivleme: Owner, Manager.</summary>
    public const string ManageContacts = nameof(ManageContacts);

    /// <summary>Cari bakiye, hareketler, ekstre, kasa, tahsilat linkleri: Owner, Manager, Accountant (salt okunur).</summary>
    public const string ViewFinance = nameof(ViewFinance);

    /// <summary>Cari/kasa kaydı, tahsilat, tahsilat linki, ekstre paylaşımı: Owner, Manager.</summary>
    public const string ManageFinance = nameof(ManageFinance);

    /// <summary>Ürün/hizmet, kategori, Excel'den ürün içe aktarma ve stok hareketi: Owner, Manager.</summary>
    public const string ManageCatalog = nameof(ManageCatalog);

    /// <summary>Excel'e aktarma: Owner, Manager, Accountant.</summary>
    public const string Export = nameof(Export);

    /// <summary>Sadece Owner (ileride maaş, abonelik).</summary>
    /// <summary>Raporlar: Sahip, Yönetici, Mali Müşavir (Personel göremez).</summary>
    public const string ViewReports = nameof(ViewReports);

    public const string OwnerOnly = nameof(OwnerOnly);

    /// <summary>Kurulum sihirbazı: işletmesi olmayan kullanıcı veya kurulumu bitmemiş işletmenin Owner'ı.</summary>
    public const string Setup = nameof(Setup);

    /// <summary>E-postası doğrulanmış kullanıcı (davet, ileride SMS gönderimi).</summary>
    public const string VerifiedEmail = nameof(VerifiedEmail);

    public static AuthorizationBuilder AddKobiFabrikaPolicies(this AuthorizationBuilder builder) => builder
        .AddPolicy(Panel, p => p.AddRequirements(new MemberRequirement()))
        .AddPolicy(CanWrite, p => p.AddRequirements(new MemberRequirement(BusinessRole.Owner, BusinessRole.Manager, BusinessRole.Staff)))
        .AddPolicy(ViewSettings, p => p.AddRequirements(new MemberRequirement(BusinessRole.Owner, BusinessRole.Manager, BusinessRole.Accountant)))
        .AddPolicy(ManageBusiness, p => p.AddRequirements(new MemberRequirement(BusinessRole.Owner, BusinessRole.Manager)))
        .AddPolicy(ManageTeam, p => p.AddRequirements(new MemberRequirement(BusinessRole.Owner, BusinessRole.Manager)))
        .AddPolicy(ViewAuditLog, p => p.AddRequirements(new MemberRequirement(BusinessRole.Owner, BusinessRole.Manager, BusinessRole.Accountant)))
        .AddPolicy(ManageContacts, p => p.AddRequirements(new MemberRequirement(RoleSets.Managers)))
        .AddPolicy(ViewFinance, p => p.AddRequirements(new MemberRequirement(RoleSets.FinanceViewers)))
        .AddPolicy(ManageFinance, p => p.AddRequirements(new MemberRequirement(RoleSets.Managers)))
        .AddPolicy(ManageCatalog, p => p.AddRequirements(new MemberRequirement(RoleSets.Managers)))
        .AddPolicy(Export, p => p.AddRequirements(new MemberRequirement(RoleSets.FinanceViewers)))
        .AddPolicy(ViewReports, p => p.AddRequirements(new MemberRequirement(RoleSets.FinanceViewers)))
        .AddPolicy(OwnerOnly, p => p.AddRequirements(new MemberRequirement(BusinessRole.Owner)))
        .AddPolicy(Setup, p => p.AddRequirements(new SetupRequirement()))
        .AddPolicy(VerifiedEmail, p => p.AddRequirements(new VerifiedEmailRequirement()));
}

/// <summary>Policy'lerde ve alan bazlı kontrollerde (ör. Staff'a bakiye gösterilmez) ortak rol kümeleri.</summary>
public static class RoleSets
{
    public static readonly BusinessRole[] Managers = [BusinessRole.Owner, BusinessRole.Manager];
    public static readonly BusinessRole[] FinanceViewers = [BusinessRole.Owner, BusinessRole.Manager, BusinessRole.Accountant];
}

/// <summary>Aktif üyelik + kurulum tamam + (varsa) izin verilen roller.</summary>
public sealed class MemberRequirement(params BusinessRole[] roles) : IAuthorizationRequirement
{
    public IReadOnlyList<BusinessRole> Roles { get; } = roles;
}

public sealed class SetupRequirement : IAuthorizationRequirement;

public sealed class VerifiedEmailRequirement : IAuthorizationRequirement;

/// <summary>Yetki reddinin Türkçe açıklaması ve frontend'in yönlendirme için kullandığı kod.</summary>
public sealed class KfFailureReason(IAuthorizationHandler handler, string code, string message)
    : AuthorizationFailureReason(handler, message)
{
    public string Code { get; } = code;
}

internal sealed class MembershipAuthorizationHandler(CurrentMember member) : IAuthorizationHandler
{
    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true || member.UserId is null)
        {
            return Task.CompletedTask;
        }

        foreach (var requirement in context.PendingRequirements.ToList())
        {
            switch (requirement)
            {
                case MemberRequirement r:
                    if (!member.IsMember || !member.SetupCompleted)
                    {
                        Fail(context, "setup_required", "Önce işletme kurulumunu tamamlayın.");
                    }
                    else if (r.Roles.Count > 0 && !r.Roles.Contains(member.Role!.Value))
                    {
                        Fail(context, "forbidden_role", "Bu işlem için yetkiniz yok.");
                    }
                    else
                    {
                        context.Succeed(requirement);
                    }

                    break;

                case SetupRequirement:
                    if (!member.IsMember || (member.Role == BusinessRole.Owner && !member.SetupCompleted))
                    {
                        context.Succeed(requirement);
                    }
                    else
                    {
                        Fail(context, "setup_completed", "İşletme kurulumu zaten tamamlandı.");
                    }

                    break;

                case VerifiedEmailRequirement:
                    if (member.EmailConfirmed)
                    {
                        context.Succeed(requirement);
                    }
                    else
                    {
                        Fail(context, "email_not_confirmed", "Bu işlem için önce e-postanı doğrulamalısın.");
                    }

                    break;
            }
        }

        return Task.CompletedTask;
    }

    private void Fail(AuthorizationHandlerContext context, string code, string message) =>
        context.Fail(new KfFailureReason(this, code, message));
}
