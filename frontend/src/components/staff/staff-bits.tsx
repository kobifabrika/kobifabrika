"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { cn } from "cn";

import { api, unwrap } from "@/lib/api/client";
import type { StaffColor } from "@/lib/api/types";

export const staffKey = ["staff"] as const;

/** Personel listesi (takvim kolonları, seçiciler). */
export function useStaffList(includeInactive = false) {
  return useQuery({
    queryKey: [...staffKey, "list", includeInactive],
    queryFn: () => unwrap(api.GET("/api/staff", { params: { query: { includeInactive } } })),
  });
}

/** Oturumdaki kullanıcıya bağlı personel kaydı. */
export function useMyStaff() {
  return useQuery({ queryKey: [...staffKey, "me"], queryFn: () => unwrap(api.GET("/api/staff/me")), staleTime: 60_000 });
}

export function useStaffRefresh() {
  const queryClient = useQueryClient();
  return () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: staffKey }),
      queryClient.invalidateQueries({ queryKey: ["calendar"] }),
    ]);
}

/** Takvim rengi: 3 renk kuralına uyan tonlar; mavi sadece zemin/şerit olarak (yazı değil). */
export const staffColorClass: Record<StaffColor, string> = {
  Black: "bg-black",
  DarkGray: "bg-neutral-700",
  Gray: "bg-neutral-400",
  LightGray: "bg-neutral-200",
  Blue: "bg-brand",
  Outline: "bg-white ring-1 ring-black ring-inset",
};

/** Personelin renk işareti (kolon başlığı, liste). */
export function StaffSwatch({ color, className }: { color: StaffColor; className?: string }) {
  return <span aria-hidden className={cn("inline-block size-3 shrink-0 rounded-full", staffColorClass[color], className)} />;
}

/** Baş harfler: "Ayşe Yılmaz" → "AY". */
export function initials(name: string): string {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((p) => p[0]!.toLocaleUpperCase("tr-TR"))
    .join("");
}
