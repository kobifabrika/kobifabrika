"use client";

import { Building2, Check, LogOut, ShieldCheck, UserRound } from "lucide-react";
import Link from "next/link";

import { useSwitchBusiness } from "@/components/panel/business-switcher";
import { useLogout, useSession } from "@/components/providers/session-provider";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { roleNames, type Role } from "@/lib/api/types";

export function initials(name: string) {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((p) => p[0]!.toLocaleUpperCase("tr-TR"))
    .join("");
}

/** Masaüstü kullanıcı menüsü: ad, rol, Hesabım, işletme değiştir (birden çok üyelik varsa), Çıkış. Telefonda MobileMenu. */
export function UserMenu() {
  const { me, business, role } = useSession();
  const logout = useLogout();
  const switchTo = useSwitchBusiness();

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        aria-label={`Kullanıcı menüsü: ${me.user.fullName}`}
        className="hidden size-11 items-center justify-center rounded-full bg-navy-800 text-sm font-bold text-white lg:flex"
      >
        {initials(me.user.fullName)}
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-72">
        <DropdownMenuLabel className="flex flex-col gap-0.5 py-2">
          <span className="text-sm font-semibold text-black">{me.user.fullName}</span>
          <span className="text-xs font-normal text-neutral-600">
            {business.name} · {roleNames[role]}
          </span>
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuItem asChild>
          <Link href="/panel/hesabim">
            <UserRound aria-hidden /> Hesabım
          </Link>
        </DropdownMenuItem>
        {me.user.isPlatformAdmin && (
          <DropdownMenuItem asChild>
            <Link href="/platform">
              <ShieldCheck aria-hidden /> Süper admin
            </Link>
          </DropdownMenuItem>
        )}
        {me.memberships.length > 1 && (
          <>
            <DropdownMenuSeparator />
            <DropdownMenuLabel className="text-xs text-neutral-600">İşletme değiştir</DropdownMenuLabel>
            {me.memberships.map((m) => (
              <DropdownMenuItem
                key={m.businessId}
                disabled={m.businessId === business.id}
                onSelect={() => switchTo(m.businessId)}
              >
                {m.businessId === business.id ? <Check aria-hidden /> : <Building2 aria-hidden />}
                <span className="flex flex-col">
                  <span>{m.businessName}</span>
                  <span className="text-xs text-neutral-600">{roleNames[m.role as Role]}</span>
                </span>
              </DropdownMenuItem>
            ))}
          </>
        )}
        <DropdownMenuSeparator />
        <DropdownMenuItem onSelect={() => logout()}>
          <LogOut aria-hidden /> Çıkış yap
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
