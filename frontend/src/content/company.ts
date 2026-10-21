/**
 * Şirket bilgisi: tek kaynak (karar 2026-10-17, soru 15). Şirket kurulunca unvan, adres ve vergi bilgisi burada ve backend'de
 * `Company:*` ayarında güncellenir; `company.test.ts` iki tarafın e-posta alt bilgisinin aynı kaldığını kontrol eder.
 * Boş alanlar (adres, vergi) sayfalarda gösterilmez.
 */
export const company = {
  /** Unvan. Şirket kurulana kadar sadece marka adı (karar 2026-10-21); kurulunca tam unvan. */
  name: "KobiFabrika",
  brand: "KobiFabrika",
  address: null as string | null,
  taxOffice: null as string | null,
  taxNumber: null as string | null,
  mersis: null as string | null,
  /** Destek ve KVKK başvuruları (Cloudflare Email Routing ile yönlendirilir). */
  email: "destek@kobifabrika.com",
  website: "https://kobifabrika.com",
} as const;

/** Sistem e-postalarının alt bilgisi; backend appsettings.json → Company:EmailFooter ile aynı olmalı. */
export const companyEmailFooter = `${company.name} · ${company.email}`;

/** Tek satır künye: "Unvan · Adres · Vergi dairesi / no". */
export function companyLine(): string {
  const tax = company.taxOffice && company.taxNumber ? `${company.taxOffice} V.D. ${company.taxNumber}` : null;
  return [company.name, company.address, tax].filter(Boolean).join(" · ");
}
