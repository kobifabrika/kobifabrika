"use client";

import { cn } from "cn";
import { ShieldAlert } from "lucide-react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import type { ReactNode } from "react";

import { useSession } from "@/components/providers/session-provider";
import { Card, CardContent } from "@/components/ui/card";
import { can, type Permission } from "@/lib/permissions";

const tabs: { href: string; label: string; permission: Permission }[] = [
  { href: "/panel/ayarlar", label: "İşletme", permission: "viewSettings" },
  { href: "/panel/ayarlar/calisma-saatleri", label: "Çalışma saatleri", permission: "viewSettings" },
  { href: "/panel/ayarlar/ekip", label: "Ekip", permission: "manageTeam" },
  { href: "/panel/ayarlar/aktivite", label: "Aktivite günlüğü", permission: "viewAuditLog" },
];

/** Ayarlar sekmeleri; role göre görünür. Telefonda yatay kaydırılır. */
export function SettingsNav() {
  const pathname = usePathname();
  const { role } = useSession();

  return (
    <nav aria-label="Ayarlar" className="-mx-4 overflow-x-auto px-4 lg:mx-0 lg:px-0">
      <ul className="flex min-w-max gap-1 border-b border-border">
        {tabs
          .filter((t) => can(role, t.permission))
          .map((t) => {
            const active = pathname === t.href;
            return (
              <li key={t.href}>
                <Link
                  href={t.href}
                  aria-current={active ? "page" : undefined}
                  className={cn(
                    "-mb-px flex min-h-11 items-center border-b-2 px-3 text-sm font-semibold whitespace-nowrap",
                    active ? "border-brand text-black" : "border-transparent text-neutral-600 hover:text-black",
                  )}
                >
                  {t.label}
                </Link>
              </li>
            );
          })}
      </ul>
    </nav>
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
