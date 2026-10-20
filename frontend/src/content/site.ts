/** Tanıtım sitesi genel bilgileri. Şirket bilgisi content/company.ts'ten. */
import { company } from "./company";


/** Slogan: sayfa başlığı, meta açıklama, site ve giriş ekranları (karar 2026-10-15; eski "işletim sistemi" ifadesi kullanılmaz). */
export const siteSlogan = "KOBİ'nin dijital fabrikası";

export type SocialKey = "instagram" | "facebook" | "youtube" | "tiktok";

/** Sosyal medya hesapları (site alt bilgisinde ikonlarıyla). */
export const siteSocial: readonly { key: SocialKey; label: string; href: string }[] = [
  { key: "instagram", label: "Instagram", href: "https://www.instagram.com/kobifabrika" },
  { key: "facebook", label: "Facebook", href: "https://www.facebook.com/kobifabrika" },
  { key: "youtube", label: "YouTube", href: "https://www.youtube.com/@kobifabrika" },
  { key: "tiktok", label: "TikTok", href: "https://www.tiktok.com/@kobifabrika" },
];

export const siteContact = {
  company: company.name,
  email: company.email,
};

/** Üst menüdeki sayfa içi bağlantılar. */
export const siteNav = [
  { href: "/#moduller", label: "Modüller" },
  { href: "/#farkimiz", label: "Farkımız" },
  { href: "/#paketler", label: "Paketler" },
  { href: "/#sss", label: "SSS" },
];
