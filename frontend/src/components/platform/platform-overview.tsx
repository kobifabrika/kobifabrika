"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";

import { QueryError } from "@/components/feedback/query-error";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { api, unwrap } from "@/lib/api/client";

import { BusinessRows } from "./platform-businesses";

export const adminKey = ["admin"] as const;

/** /platform: özet sayılar (KVKK: sadece sayılar), son kayıtlar, açık geri bildirim. */
export function PlatformOverview() {
  const query = useQuery({
    queryKey: [...adminKey, "overview"],
    queryFn: () => unwrap(api.GET("/api/admin/overview")),
  });
  if (query.isPending)
    return (
      <p role="status" className="text-sm text-neutral-600">
        Yükleniyor…
      </p>
    );
  if (query.isError) return <QueryError error={query.error} onRetry={query.refetch} />;
  const o = query.data;
  const stats: { label: string; value: number; href: string }[] = [
    { label: "İşletme", value: o.businesses, href: "/platform/isletmeler" },
    { label: "Son 7 gün yeni", value: o.newLast7Days, href: "/platform/isletmeler" },
    { label: "Pilot", value: o.pilot, href: "/platform/isletmeler?filtre=pilot" },
    { label: "Deneme", value: o.trial, href: "/platform/isletmeler?filtre=trial" },
    { label: "Çırak", value: o.cirak, href: "/platform/isletmeler?filtre=cirak" },
    { label: "Kalfa", value: o.kalfa, href: "/platform/isletmeler?filtre=kalfa" },
    { label: "Usta", value: o.usta, href: "/platform/isletmeler?filtre=usta" },
    { label: "Salt okunur", value: o.readOnly, href: "/platform/isletmeler?filtre=readonly" },
    { label: "Açık geri bildirim", value: o.openFeedback, href: "/platform/geri-bildirim" },
  ];
  return (
    <div className="flex flex-col gap-6">
      <h1 className="text-2xl font-bold tracking-tight text-black">Özet</h1>
      <section aria-label="Sayılar" className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
        {stats.map((s) => (
          <Link key={s.label} href={s.href} className="block hover:[&>div]:border-black">
            <Card className="h-full gap-1 py-4">
              <CardContent className="flex flex-col gap-1 px-4">
                <span className="text-xs text-neutral-600">{s.label}</span>
                <span className="text-2xl font-bold text-black tabular-nums">{s.value}</span>
              </CardContent>
            </Card>
          </Link>
        ))}
      </section>
      <Card className="gap-3 py-4">
        <CardHeader className="px-4">
          <CardTitle className="text-base font-semibold text-black">Son kayıtlar</CardTitle>
        </CardHeader>
        <CardContent className="px-4">
          {o.recent.length === 0 ? (
            <p className="text-sm text-neutral-600">Henüz işletme yok.</p>
          ) : (
            <BusinessRows items={o.recent} />
          )}
        </CardContent>
      </Card>
    </div>
  );
}
