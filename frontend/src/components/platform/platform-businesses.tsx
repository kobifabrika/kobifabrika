"use client";

import { useQuery } from "@tanstack/react-query";
import { Search } from "lucide-react";
import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useState } from "react";

import { useDebounced } from "@/components/catalog/product-list";
import { QueryError } from "@/components/feedback/query-error";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Chip, ScrollRow } from "@/components/ui/scroll-row";
import { api, unwrap } from "@/lib/api/client";
import type { AdminBusinessItem } from "@/lib/api/types";
import { formatDate, formatRelative } from "@/lib/format";

const filters = [
  { key: "", label: "Hepsi" },
  { key: "pilot", label: "Pilot" },
  { key: "trial", label: "Deneme" },
  { key: "cirak", label: "Çırak" },
  { key: "kalfa", label: "Kalfa" },
  { key: "usta", label: "Usta" },
  { key: "readonly", label: "Salt okunur" },
] as const;

const pageSize = 50;

/** "Pilot", "Deneme · bitiş 23.10.2026", "Kalfa" + salt okunur. */
export function planLabel(b: Pick<AdminBusinessItem, "status" | "planName" | "trialEndsAt" | "isReadOnly">): string {
  const base =
    b.status === "Pilot"
      ? "Pilot"
      : b.status === "Trial"
        ? `Deneme${b.trialEndsAt ? ` · bitiş ${formatDate(b.trialEndsAt)}` : ""}`
        : b.planName;
  return b.isReadOnly ? `${base} · salt okunur` : base;
}

/** /platform/isletmeler?ara=&filtre=: arama (ad, slug, sahip e-postası), filtre çipleri, tablo (masaüstü) / kart (telefon). */
export function PlatformBusinesses() {
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();
  const filter = params.get("filtre") ?? "";
  const [search, setSearch] = useState(params.get("ara") ?? "");
  const term = useDebounced(search.trim(), 300);
  const [page, setPage] = useState(1);
  const query = useQuery({
    queryKey: ["admin", "businesses", term, filter, page],
    queryFn: () =>
      unwrap(
        api.GET("/api/admin/businesses", {
          params: { query: { search: term || undefined, filter: filter || undefined, page, pageSize } },
        }),
      ),
  });

  const setFilter = (key: string) => {
    setPage(1);
    const p = new URLSearchParams(params.toString());
    if (key) p.set("filtre", key);
    else p.delete("filtre");
    router.replace(`${pathname}?${p.toString()}`, { scroll: false });
  };

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-2xl font-bold tracking-tight text-black">İşletmeler</h1>
      <div className="relative">
        <Search
          aria-hidden
          className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-neutral-500"
        />
        <Input
          type="search"
          aria-label="İşletme ara"
          placeholder="Ad, link adı veya sahip e-postası"
          className="pl-9"
          value={search}
          onChange={(e) => {
            setSearch(e.target.value);
            setPage(1);
          }}
        />
      </div>
      <ScrollRow aria-label="Filtre">
        {filters.map((f) => (
          <Chip key={f.key} active={filter === f.key} onClick={() => setFilter(f.key)}>
            {f.label}
          </Chip>
        ))}
      </ScrollRow>
      {query.isPending ? (
        <p role="status" className="text-sm text-neutral-600">
          Yükleniyor…
        </p>
      ) : query.isError ? (
        <QueryError error={query.error} onRetry={query.refetch} />
      ) : query.data.items.length === 0 ? (
        <p className="text-sm text-neutral-600">Sonuç yok.</p>
      ) : (
        <>
          <p className="text-sm text-neutral-600 tabular-nums">{query.data.total} işletme</p>
          <BusinessRows items={query.data.items} />
          {query.data.total > pageSize && (
            <div className="flex items-center gap-2">
              <Button variant="outline" disabled={page === 1} onClick={() => setPage((p) => p - 1)}>
                Önceki
              </Button>
              <span className="text-sm text-neutral-600 tabular-nums">
                {page}/{Math.ceil(query.data.total / pageSize)}
              </span>
              <Button
                variant="outline"
                disabled={page * pageSize >= query.data.total}
                onClick={() => setPage((p) => p + 1)}
              >
                Sonraki
              </Button>
            </div>
          )}
        </>
      )}
    </div>
  );
}

/** İşletme satırları: masaüstünde tablo, telefonda kart listesi. */
export function BusinessRows({ items }: { items: AdminBusinessItem[] }) {
  return (
    <>
      <table className="hidden w-full text-sm md:table">
        <thead>
          <tr className="border-b border-border text-left text-xs text-neutral-600">
            <th className="py-2 pr-3 font-medium">İşletme</th>
            <th className="py-2 pr-3 font-medium">Paket</th>
            <th className="py-2 pr-3 font-medium">Kayıt</th>
            <th className="py-2 pr-3 font-medium">Son giriş</th>
            <th className="py-2 pr-3 text-right font-medium">Kullanıcı</th>
            <th className="py-2 pr-3 text-right font-medium">Müşteri</th>
            <th className="py-2 text-right font-medium">Bu ay randevu</th>
          </tr>
        </thead>
        <tbody>
          {items.map((b) => (
            <tr key={b.id} className="border-b border-border">
              <td className="py-2 pr-3">
                <Link
                  href={`/platform/isletmeler/${b.id}`}
                  className="font-semibold text-black underline-offset-4 hover:underline"
                >
                  {b.name}
                </Link>
                <span className="block text-xs text-neutral-600">
                  {b.ownerEmail ?? "Sahip yok"}
                  {!b.setupCompleted && " · kurulum bitmedi"}
                </span>
              </td>
              <td className="py-2 pr-3 text-black">{planLabel(b)}</td>
              <td className="py-2 pr-3 text-neutral-700 tabular-nums">{formatDate(b.createdAt)}</td>
              <td className="py-2 pr-3 text-neutral-700">{b.lastLoginAt ? formatRelative(b.lastLoginAt) : "—"}</td>
              <td className="py-2 pr-3 text-right tabular-nums">{b.users}</td>
              <td className="py-2 pr-3 text-right tabular-nums">{b.contacts}</td>
              <td className="py-2 text-right tabular-nums">{b.appointmentsThisMonth}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <ul className="flex flex-col divide-y divide-border border-y border-border md:hidden">
        {items.map((b) => (
          <li key={b.id}>
            <Link href={`/platform/isletmeler/${b.id}`} className="flex min-h-14 flex-col gap-0.5 py-3">
              <span className="text-sm font-semibold text-black">{b.name}</span>
              <span className="text-xs text-neutral-700">{planLabel(b)}</span>
              <span className="text-xs text-neutral-600 tabular-nums">
                {b.users} kullanıcı · {b.contacts} müşteri · bu ay {b.appointmentsThisMonth} randevu
              </span>
              <span className="text-xs text-neutral-600">
                Son giriş: {b.lastLoginAt ? formatRelative(b.lastLoginAt) : "yok"}
              </span>
            </Link>
          </li>
        ))}
      </ul>
    </>
  );
}
