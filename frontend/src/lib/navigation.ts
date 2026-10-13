import {
  Banknote,
  CalendarDays,
  CalendarPlus,
  FileText,
  ReceiptText,
  UserPlus,
  ChartColumn,
  Clock,
  House,
  IdCard,
  Megaphone,
  Package,
  Boxes,
  Receipt,
  Settings,
  Users,
  Wallet,
  type LucideIcon,
} from "lucide-react";

import type { Role } from "@/lib/api/types";
import { can, type Permission } from "@/lib/permissions";

export type NavItem = {
  /** URL parçası; `/panel/{slug}`. Bugün ekranı için boş. */
  slug: string;
  label: string;
  icon: LucideIcon;
  /** Ekranın geleceği sprint (iskelet sayfasında gösterilir). */
  sprint?: number;
  /** Şablona bağlı modül; işletmede kapalıysa menüde görünmez (Ayarlar → Modüller). */
  module?: "appointments" | "stock";
  /** Görmek için gereken yetki (src/lib/permissions.ts). */
  permission?: Permission;
};

export type NavGroup = { title: string; items: NavItem[] };

/** Panel menüsü (docs/04-tasarim.md → Menü, Faz 1). */
export const navGroups: NavGroup[] = [
  {
    title: "İşletme",
    items: [
      { slug: "", label: "Bugün", icon: House, sprint: 4 },
      { slug: "randevular", label: "Randevular", icon: CalendarDays, sprint: 5, module: "appointments" },
      { slug: "satislar", label: "Satışlar", icon: Receipt, sprint: 3 },
      { slug: "musteriler", label: "Müşteriler", icon: Users, sprint: 2 },
      { slug: "urunler", label: "Ürünler/Hizmetler", icon: Package, sprint: 3 },
      { slug: "stok", label: "Stok", icon: Boxes, sprint: 3, module: "stock" },
      { slug: "cari-kasa", label: "Cari ve Kasa", icon: Wallet, sprint: 2, permission: "viewFinance" },
      { slug: "kampanyalar", label: "Kampanyalar", icon: Megaphone, sprint: 6 },
    ],
  },
  {
    title: "Yönetim",
    items: [
      { slug: "personel", label: "Personel", icon: IdCard, sprint: 5, permission: "viewStaff" },
      { slug: "mesai-izin", label: "Mesai ve İzin", icon: Clock, sprint: 7 },
      { slug: "raporlar", label: "Raporlar", icon: ChartColumn, sprint: 4, permission: "viewReports" },
      { slug: "ayarlar", label: "Ayarlar", icon: Settings, sprint: 1, permission: "viewSettings" },
    ],
  },
];

export const allNavItems = navGroups.flatMap((g) => g.items);

export type Modules = { appointments: boolean; stock: boolean };

/** Role ve işletmenin açık modüllerine göre görünür menü (şablon: Hizmet'te Stok, Perakende'de Randevular kapalı). */
export function visibleNavGroups(role: Role | null | undefined, modules: Modules | null | undefined): NavGroup[] {
  return navGroups
    .map((group) => ({
      ...group,
      items: group.items.filter(
        (item) =>
          (!item.module || (modules?.[item.module] ?? true)) && (!item.permission || can(role, item.permission)),
      ),
    }))
    .filter((group) => group.items.length > 0);
}

export const panelHref = (slug: string) => (slug ? `/panel/${slug}` : "/panel");

export function findNavItem(slug: string) {
  return allNavItems.find((item) => item.slug === slug);
}

/** Telefondaki alt sekme çubuğu: Bugün, Randevu, (+), Satış, Müşteri, Menü. */
export const bottomTabs: readonly { slug: string; label: string; icon: LucideIcon; module?: NavItem["module"] }[] = [
  { slug: "", label: "Bugün", icon: House },
  { slug: "randevular", label: "Randevu", icon: CalendarDays, module: "appointments" },
  { slug: "satislar", label: "Satış", icon: Receipt },
  { slug: "musteriler", label: "Müşteri", icon: Users },
];

export type QuickAction = {
  label: string;
  href: string;
  icon: LucideIcon;
  module?: NavItem["module"];
  permission?: Permission;
  /** Ctrl+K eşleşmesi için ek kelimeler. */
  keywords?: string;
};

/** Hızlı işlemler: telefonda (+) menüsü, masaüstünde üst çubuktaki (+) ve Ctrl+K aynı listeyi kullanır. */
export const quickActions: readonly QuickAction[] = [
  { label: "Yeni satış", href: "/panel/satislar/yeni", icon: ReceiptText, permission: "createSales", keywords: "satış sepet barkod fiş kasa" },
  { label: "Yeni teklif", href: "/panel/teklifler/yeni", icon: FileText, permission: "createSales", keywords: "teklif fiyat" },
  { label: "Yeni randevu", href: "/panel/randevular?yeni=1", icon: CalendarPlus, module: "appointments", permission: "writeAppointments", keywords: "randevu takvim" },
  { label: "Yeni müşteri", href: "/panel/musteriler/yeni", icon: UserPlus, permission: "writeContacts", keywords: "müşteri ekle" },
  { label: "Tahsilat", href: "/panel/cari-kasa?sekme=bakiyeler", icon: Banknote, permission: "manageFinance", keywords: "tahsilat ödeme" },
  { label: "Gider ekle", href: "/panel/cari-kasa/gelir-gider/yeni?tur=gider", icon: ReceiptText, permission: "manageFinance", keywords: "gider masraf fatura kira" },
];

export function visibleQuickActions(role: Role | null | undefined, modules: Modules | null | undefined): QuickAction[] {
  return quickActions.filter(
    (a) => (!a.module || (modules?.[a.module] ?? true)) && (!a.permission || can(role, a.permission)),
  );
}

export function isActive(pathname: string, slug: string) {
  const href = panelHref(slug);
  return slug ? pathname === href || pathname.startsWith(`${href}/`) : pathname === href;
}
