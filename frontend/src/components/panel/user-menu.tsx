"use client";

import { useQueryClient } from "@tanstack/react-query";
import { Building2, Check, LogOut, UserRound } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { toast } from "sonner";

import { meQueryKey, useLogout, useSession } from "@/components/providers/session-provider";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { api, errorMessage, unwrap } from "@/lib/api/client";
import { roleNames, type Role } from "@/lib/api/types";

function initials(name: string) {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((p) => p[0]!.toLocaleUpperCase("tr-TR"))
    .join("");
}

/** Kullanıcı menüsü: ad, rol, Hesabım, işletme değiştir (birden çok üyelik varsa), Çıkış. */
export function UserMenu() {
  const { me, business, role } = useSession();
  const logout = useLogout();
  const queryClient = useQueryClient();
  const router = useRouter();

  const switchTo = async (businessId: string) => {
    try {
      const next = await unwrap(api.POST("/api/auth/switch-business", { body: { businessId } }));
      queryClient.clear();
      queryClient.setQueryData(meQueryKey, next);
      router.push("/panel");
      toast.success(`${next.business?.name} işletmesine geçildi.`);
    } catch (e) {
      toast.error(errorMessage(e));
    }
  };

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        aria-label={`Kullanıcı menüsü: ${me.user.fullName}`}
        className="flex size-11 items-center justify-center rounded-full bg-brand-300 text-sm font-bold text-brand-900"
      >
        {initials(me.user.fullName)}
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-72">
        <DropdownMenuLabel className="flex flex-col gap-0.5 py-2">
          <span className="text-sm font-semibold text-brand-900">{me.user.fullName}</span>
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
