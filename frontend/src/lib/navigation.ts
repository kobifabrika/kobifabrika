import {
  CalendarDays,
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

export type NavItem = {
  /** URL parçası; `/panel/{slug}`. Bugün ekranı için boş. */
  slug: string;
  label: string;
  icon: LucideIcon;
  /** Ekranın geleceği sprint (iskelet sayfasında gösterilir). */
  sprint?: number;
};

export type NavGroup = { title: string; items: NavItem[] };

/** Panel menüsü (docs/04-tasarim.md → Menü, Faz 1). */
export const navGroups: NavGroup[] = [
  {
    title: "İşletme",
    items: [
      { slug: "", label: "Bugün", icon: House, sprint: 4 },
      { slug: "randevular", label: "Randevular", icon: CalendarDays, sprint: 5 },
      { slug: "satislar", label: "Satışlar", icon: Receipt, sprint: 3 },
      { slug: "musteriler", label: "Müşteriler", icon: Users, sprint: 2 },
      { slug: "urunler", label: "Ürünler/Hizmetler", icon: Package, sprint: 3 },
      { slug: "stok", label: "Stok", icon: Boxes, sprint: 3 },
      { slug: "cari-kasa", label: "Cari ve Kasa", icon: Wallet, sprint: 4 },
      { slug: "kampanyalar", label: "Kampanyalar", icon: Megaphone, sprint: 6 },
    ],
  },
  {
    title: "Yönetim",
    items: [
      { slug: "personel", label: "Personel", icon: IdCard, sprint: 7 },
      { slug: "mesai-izin", label: "Mesai ve İzin", icon: Clock, sprint: 7 },
      { slug: "raporlar", label: "Raporlar", icon: ChartColumn, sprint: 7 },
      { slug: "ayarlar", label: "Ayarlar", icon: Settings, sprint: 1 },
    ],
  },
];

export const allNavItems = navGroups.flatMap((g) => g.items);

export const panelHref = (slug: string) => (slug ? `/panel/${slug}` : "/panel");

export function findNavItem(slug: string) {
  return allNavItems.find((item) => item.slug === slug);
}

/** Telefondaki alt sekme çubuğu: Bugün, Randevu, (+), Satış, Müşteri, Menü. */
export const bottomTabs = [
  { slug: "", label: "Bugün", icon: House },
  { slug: "randevular", label: "Randevu", icon: CalendarDays },
  { slug: "satislar", label: "Satış", icon: Receipt },
  { slug: "musteriler", label: "Müşteri", icon: Users },
] as const;

export function isActive(pathname: string, slug: string) {
  const href = panelHref(slug);
  return slug ? pathname === href || pathname.startsWith(`${href}/`) : pathname === href;
}
