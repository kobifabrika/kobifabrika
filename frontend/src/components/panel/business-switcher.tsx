"use client";

import { useQueryClient } from "@tanstack/react-query";
import { cn } from "cn";
import { Building2, Check, ChevronsUpDown } from "lucide-react";
import { useRouter } from "next/navigation";
import { useCallback } from "react";
import { toast } from "sonner";

import { meQueryKey, useSession } from "@/components/providers/session-provider";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { api, errorMessage, fileUrl, unwrap } from "@/lib/api/client";
import { roleNames, type Role } from "@/lib/api/types";

/** POST /api/auth/switch-business: önbelleği temizler, yeni oturumu yazar, Bugün'e döner. */
export function useSwitchBusiness() {
  const queryClient = useQueryClient();
  const router = useRouter();
  return useCallback(
    async (businessId: string) => {
      try {
        const next = await unwrap(api.POST("/api/auth/switch-business", { body: { businessId } }));
        queryClient.clear();
        queryClient.setQueryData(meQueryKey, next);
        router.push("/panel");
        toast.success(`${next.business?.name} işletmesine geçildi.`);
      } catch (e) {
        toast.error(errorMessage(e));
      }
    },
    [queryClient, router],
  );
}

type Variant = "sidebar" | "topbar";

/**
 * Aktif işletmenin adı (logo varsa küçük logoyla). Uzun ad tek satırda "…" ile kısalır, tamamı title'da.
 * Birden fazla üyelik varsa ada tıklayınca işletme değiştirme menüsü açılır.
 */
export function BusinessSwitcher({ variant, className }: { variant: Variant; className?: string }) {
  const { me, business } = useSession();
  const switchTo = useSwitchBusiness();
  const logo = fileUrl(business.logoUrl);
  const switchable = me.memberships.length > 1;

  const content = (
    <>
      {logo && (
        // eslint-disable-next-line @next/next/no-img-element -- API'den gelen kullanıcı dosyası; next/image optimizasyonu gerekmez.
        <img
          src={logo}
          alt=""
          className={cn("shrink-0 rounded bg-white object-contain", variant === "sidebar" ? "size-8" : "size-7")}
        />
      )}
      <span className="min-w-0 flex-1 truncate text-left text-base font-semibold text-white">{business.name}</span>
      {switchable && <ChevronsUpDown aria-hidden className="size-4 shrink-0 text-neutral-400" />}
    </>
  );

  const base = cn(
    "flex min-h-11 min-w-0 items-center gap-2.5 rounded-md",
    variant === "sidebar" ? "w-full px-3" : "px-1",
    className,
  );

  if (!switchable) {
    return (
      <div className={base} title={business.name} data-testid="business-name">
        {content}
      </div>
    );
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        title={business.name}
        aria-label={`İşletme: ${business.name}. İşletme değiştir`}
        className={cn(base, "outline-sidebar-ring transition-colors hover:bg-sidebar-accent")}
      >
        {content}
      </DropdownMenuTrigger>
      <DropdownMenuContent align="start" className="w-72">
        <DropdownMenuLabel className="text-xs text-neutral-600">İşletme değiştir</DropdownMenuLabel>
        {me.memberships.map((m) => {
          const current = m.businessId === business.id;
          return (
            <DropdownMenuItem key={m.businessId} disabled={current} onSelect={() => switchTo(m.businessId)}>
              {current ? <Check aria-hidden /> : <Building2 aria-hidden />}
              <span className="flex min-w-0 flex-col">
                <span className="truncate">{m.businessName}</span>
                <span className="text-xs text-neutral-600">{roleNames[m.role as Role]}</span>
              </span>
            </DropdownMenuItem>
          );
        })}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
