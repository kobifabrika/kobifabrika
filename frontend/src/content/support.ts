/**
 * KobiFabrika destek iletişimi (karar 2026-10-17). Ortam değişkeniyle değiştirilebilir (.env.example); boş bırakılırsa bu
 * varsayılanlar geçerlidir.
 */
export const supportWhatsapp = (process.env.NEXT_PUBLIC_SUPPORT_WHATSAPP || "905359779382").replace(/\D/g, "");
export const supportEmail = (process.env.NEXT_PUBLIC_SUPPORT_EMAIL || "destek@kobifabrika.com").trim();
/** Ekranda gösterilen biçim: +90 535 977 93 82. */
export const supportWhatsappDisplay = supportWhatsapp.replace(
  /^(\d{2})(\d{3})(\d{3})(\d{2})(\d{2})$/,
  "+$1 $2 $3 $4 $5",
);

export function supportWhatsappLink(text: string): string | null {
  return supportWhatsapp ? `https://wa.me/${supportWhatsapp}?text=${encodeURIComponent(text)}` : null;
}
