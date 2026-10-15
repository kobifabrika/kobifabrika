import type { Role } from "@/lib/api/types";

/**
 * Arayüzdeki görünürlük matrisi. Gerçek yetki kontrolü backend policy'lerinde
 * (backend/src/KobiFabrika.Api/Common/Authorization/Policies.cs); burası sadece menü ve butonları gizler.
 */
const matrix = {
  /** Ayarlar sayfası (Accountant salt okunur). */
  viewSettings: ["Owner", "Manager", "Accountant"],
  manageBusiness: ["Owner", "Manager"],
  manageTeam: ["Owner", "Manager"],
  viewAuditLog: ["Owner", "Manager", "Accountant"],
  /** Finans raporları: Staff göremez (docs/01-faz1-kapsam.md, A). */
  viewReports: ["Owner", "Manager", "Accountant"],
  /** Müşteri ekle/düzenle, not, etiket, hatırlatma, içe aktarma (Mali Müşavir salt okunur). */
  writeContacts: ["Owner", "Manager", "Staff"],
  /** Müşteri silme ve arşivleme. */
  manageContacts: ["Owner", "Manager"],
  /** Cari bakiye, hareketler, ekstre, kasa, tahsilat linkleri (Personel göremez). */
  viewFinance: ["Owner", "Manager", "Accountant"],
  /** Borç/alacak, tahsilat, tahsilat linki, ekstre paylaşımı. */
  manageFinance: ["Owner", "Manager"],
  /** Ürün/hizmet, kategori, Excel'den ürün aktarma ve stok hareketi. */
  manageCatalog: ["Owner", "Manager"],
  /** Alış fiyatı ve birim maliyet (Personel göremez; API null döner). */
  viewPurchasePrice: ["Owner", "Manager", "Accountant"],
  /** Satış yapma ve teklif hazırlama (indirim dahil). */
  createSales: ["Owner", "Manager", "Staff"],
  /** İşletmenin tüm satış ve teklifleri; Personel sadece kendi girdiklerini görür. */
  viewAllSales: ["Owner", "Manager", "Accountant"],
  /** Satış iptali (stok, kasa ve cari ters kayıtla geri alınır). */
  cancelSales: ["Owner", "Manager"],
  /** Excel'e aktarma. */
  exportData: ["Owner", "Manager", "Accountant"],
  /** Personel ekranı (Mali Müşavir salt okunur; Personel menüde görmez). */
  viewStaff: ["Owner", "Manager", "Accountant"],
  /** Personel ekleme/düzenleme, çalışma saatleri, hizmetler, izin; tatil günleri ve randevu ayarları. */
  manageStaff: ["Owner", "Manager"],
  /** Randevu açma, taşıma, durum değiştirme (karar a: Personel herkes için; ayar açıksa sadece kendisi). */
  writeAppointments: ["Owner", "Manager", "Staff"],
  /** Kampanyalar, SMS kredisi ve hareketleri (Mali Müşavir salt okunur; Personel görmez). */
  viewMarketing: ["Owner", "Manager", "Accountant"],
  /** Kampanya oluşturma, zamanlama, iptal; boş saat ve risk listesinden kampanya. */
  manageMarketing: ["Owner", "Manager"],
  /** KVKK: müşterinin verilerini dışa aktarma. */
  exportContactData: ["Owner", "Manager"],
  /** KVKK: müşterinin kişisel verilerini silme (anonimleştirme; geri alınamaz). */
  anonymizeContacts: ["Owner"],
  /** Personel maaşı ve ödenen maaşlar (Mali Müşavir salt okunur; Yönetici ve Personel göremez, API alanı hiç döndürmez). */
  viewSalary: ["Owner", "Accountant"],
  /** Maaşı düzenleme ve maaş gideri girme. */
  manageSalary: ["Owner"],
  /** Herkesin mesai kayıtları, izin talepleri ve bakiyeleri (Personel sadece kendisininkini görür). */
  viewStaffTime: ["Owner", "Manager", "Accountant"],
  /** Başkası adına mesai, mesai düzeltme, izin onay/ret, bakiye düzeltme, izin türleri ve fazla mesai eşiği. */
  manageStaffTime: ["Owner", "Manager"],
  /** Kendi mesaisine giriş/çıkış ve kendine izin talebi (bağlı personel kaydı gerekir). */
  clockInOut: ["Owner", "Manager", "Staff"],
} as const satisfies Record<string, readonly Role[]>;

export type Permission = keyof typeof matrix;

export function can(role: Role | null | undefined, permission: Permission): boolean {
  return !!role && (matrix[permission] as readonly Role[]).includes(role);
}
