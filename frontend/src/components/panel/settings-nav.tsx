"use client";

import { ShieldAlert } from "lucide-react";
import { usePathname } from "next/navigation";
import type { ReactNode } from "react";

import { useSession } from "@/components/providers/session-provider";
import { Card, CardContent } from "@/components/ui/card";
import { TabNav } from "@/components/ui/scroll-row";
import { can, type Permission } from "@/lib/permissions";

const tabs: { href: string; label: string; permission: Permission; module?: "appointments" }[] = [
  { href: "/panel/ayarlar", label: "İşletme", permission: "viewSettings" },
  { href: "/panel/ayarlar/calisma-saatleri", label: "Çalışma saatleri", permission: "viewSettings" },
  { href: "/panel/ayarlar/randevu", label: "Randevu", permission: "viewSettings", module: "appointments" },
  { href: "/panel/ayarlar/finans", label: "Finans", permission: "viewFinance" },
  { href: "/panel/ayarlar/sms", label: "SMS", permission: "viewMarketing" },
  { href: "/panel/ayarlar/ekip", label: "Ekip", permission: "manageTeam" },
  { href: "/panel/ayarlar/aktivite", label: "Aktivite günlüğü", permission: "viewAuditLog" },
];

/** Ayarlar sekmeleri; role göre görünür. Telefonda yatay kaydırılır. */
export function SettingsNav() {
  const pathname = usePathname();
  const { role, business } = useSession();

  const visible = tabs.filter((t) => can(role, t.permission) && (!t.module || business.modules[t.module]));
  return (
    <TabNav
      label="Ayarlar"
      active={pathname}
      tabs={visible.map((t) => ({ key: t.href, label: t.label, href: t.href }))}
    />
  );
}

/** Yetkisi olmayan role sayfa yerine açıklama gösterir (asıl kontrol API'de). */
export function RequirePermission({ permission, children }: { permission: Permission; children: ReactNode }) {
  const { role } = useSession();
  if (can(role, permission)) return children;
  return (
    <Card>
      <CardContent className="flex flex-col items-center gap-3 py-10 text-center">
        <ShieldAlert aria-hidden className="size-10 text-neutral-400" />
        <p className="text-base font-semibold text-black">Bu sayfayı görme yetkin yok</p>
        <p className="text-sm text-neutral-600">Gerekirse işletme sahibinden yetki iste.</p>
      </CardContent>
    </Card>
  );
}
