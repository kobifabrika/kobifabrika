"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { createContext, useCallback, useContext, type ReactNode } from "react";

import { api, unwrap } from "@/lib/api/client";
import type { Me, MeBusiness, Role } from "@/lib/api/types";

export const meQueryKey = ["auth", "me"] as const;

/** Oturumdaki kullanıcı (GET /api/auth/me). 401'de hata döner; yeniden denemez. */
export function useMeQuery() {
  return useQuery({
    queryKey: meQueryKey,
    queryFn: () => unwrap(api.GET("/api/auth/me")),
    retry: false,
    staleTime: 60_000,
  });
}

type Session = { me: Me; business: MeBusiness; role: Role };

const SessionContext = createContext<Session | null>(null);

/** Panel içinde: kurulumu bitmiş işletmesi olan oturum (PanelGate sağlar). */
export function SessionProvider({ me, children }: { me: Me; children: ReactNode }) {
  const value = me.business && me.role ? { me, business: me.business, role: me.role as Role } : null;
  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export function useSession(): Session {
  const session = useContext(SessionContext);
  if (!session) throw new Error("useSession sadece panel içinde kullanılabilir.");
  return session;
}

/** Çıkış: cookie silinir, önbellek temizlenir, giriş ekranına dönülür. */
export function useLogout() {
  const queryClient = useQueryClient();
  const router = useRouter();
  return useCallback(async () => {
    await api.POST("/api/auth/logout").catch(() => undefined);
    queryClient.clear();
    router.replace("/giris");
  }, [queryClient, router]);
}
