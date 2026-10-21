import { supportWhatsappLink } from "./support";

/**
 * Sadece-site modu (karar 2026-10-21, Sprint 8B ara adım): NEXT_PUBLIC_SITE_ONLY=1 ile derlenen frontend yalnızca tanıtım sitesini
 * sunar; API yoktur. Kayıt/giriş çağrıları "Pilot listesine katıl" olur ve WhatsApp'a gider, uygulama yolları "Çok yakında"
 * sayfasını gösterir (proxy.ts). Değer derlemeye gömülür; kapalıyken (varsayılan) her şey bugünkü gibi.
 */
export function isSiteOnly(): boolean {
  return process.env.NEXT_PUBLIC_SITE_ONLY === "1";
}

export const pilotCtaLabel = "Pilot listesine katıl";

/** Pilot listesi için hazır mesajlı WhatsApp linki; paket seçildiyse mesajda adı geçer. */
export function pilotLink(planName?: string): string {
  const text = planName
    ? `Merhaba, KobiFabrika pilot listesine ${planName} paketiyle katılmak istiyorum. İşletmem:`
    : "Merhaba, KobiFabrika pilot listesine katılmak istiyorum. İşletmem:";
  return supportWhatsappLink(text) ?? "/iletisim";
}
