/**
 * KobiFabrika destek iletişimi (build sırasında ortam değişkeninden; .env.example). Numara ve e-posta henüz yok:
 * ikisi de boşsa ekranlar düğme yerine "destek ekibine ulaşın" metni gösterir.
 */
export const supportWhatsapp = (process.env.NEXT_PUBLIC_SUPPORT_WHATSAPP ?? "").replace(/\D/g, "");
export const supportEmail = (process.env.NEXT_PUBLIC_SUPPORT_EMAIL ?? "").trim();

export function supportWhatsappLink(text: string): string | null {
  return supportWhatsapp ? `https://wa.me/${supportWhatsapp}?text=${encodeURIComponent(text)}` : null;
}
