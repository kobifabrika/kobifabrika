/**
 * KVKK aydınlatma metni — TASLAK. Hukuki metin Sprint 8'de (KVKK metinleri) son halini alacak.
 * Metin değişince sürümü artırın ve backend'deki KvkkNotice.Version'ı aynı yapın
 * (backend/src/KobiFabrika.Api/Features/Auth/AuthEndpoints.cs).
 */
export const KVKK_VERSION = "2026-10";

export const kvkkTitle = "Kişisel Verilerin İşlenmesine İlişkin Aydınlatma Metni";

export const kvkkSections: { heading: string; body: string }[] = [
  {
    heading: "Veri sorumlusu",
    body: "KobiFabrika Yazılım A.Ş. (\"KobiFabrika\"), 6698 sayılı Kişisel Verilerin Korunması Kanunu (\"KVKK\") kapsamında veri sorumlusudur.",
  },
  {
    heading: "İşlenen kişisel veriler",
    body: "Ad soyad, e-posta adresi, telefon numarası, şifrenizin geri döndürülemez özeti, işletme bilgileri, oturum ve işlem kayıtları (IP adresi, tarih ve saat).",
  },
  {
    heading: "İşleme amaçları",
    body: "Hesabınızın oluşturulması ve güvenliği, hizmetin sunulması, destek taleplerinin yanıtlanması, yasal yükümlülüklerin yerine getirilmesi ve hizmetle ilgili bilgilendirmeler.",
  },
  {
    heading: "Hukuki sebep",
    body: "KVKK m.5/2 (c) sözleşmenin kurulması ve ifası, (ç) hukuki yükümlülük ve (f) meşru menfaat.",
  },
  {
    heading: "Aktarım",
    body: "Verileriniz hizmetin sunulması için gerekli olduğu ölçüde barındırma, e-posta ve SMS hizmeti aldığımız tedarikçilere ve yasal olarak yetkili kurumlara aktarılabilir.",
  },
  {
    heading: "Haklarınız",
    body: "KVKK m.11 kapsamındaki haklarınız (bilgi talep etme, düzeltme, silme, itiraz vb.) için kvkk@kobifabrika.com adresine yazabilirsiniz.",
  },
];
