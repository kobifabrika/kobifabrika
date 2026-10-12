"use client";

import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, Boxes, ChevronRight, HandCoins, Landmark, ReceiptText, Wallet, type LucideIcon } from "lucide-react";
import Link from "next/link";
import { useState, type ReactNode } from "react";
import { Bar, BarChart, CartesianGrid, XAxis, YAxis } from "recharts";

import { QueryError } from "@/components/feedback/query-error";
import { AccountBalance, accountHref } from "@/components/finance/cash-accounts";
import { CollectionDialog } from "@/components/finance/finance-dialogs";
import { useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { type ChartConfig, ChartContainer, ChartTooltip, ChartTooltipContent } from "@/components/ui/chart";
import { api, unwrap } from "@/lib/api/client";
import { type Dashboard, paymentMethodNames } from "@/lib/api/types";
import { formatMoney, istanbulParts } from "@/lib/format";
import { can } from "@/lib/permissions";

const chartConfig = { total: { label: "Satış", color: "var(--color-brand)" } } satisfies ChartConfig;

const compactTl = new Intl.NumberFormat("tr-TR", { notation: "compact", maximumFractionDigits: 1 });

/** "2026-10-12" → "12.10" (grafik ekseni). */
const shortDay = (day: string) => `${day.slice(8, 10)}.${day.slice(5, 7)}`;

function StatCard({ label, icon: Icon, value, children, href }: { label: string; icon: LucideIcon; value: string; children?: ReactNode; href?: string }) {
  const body = (
    <Card className="h-full gap-2 py-4">
      <CardHeader className="flex flex-row items-center justify-between gap-2 px-4">
        <CardTitle className="text-xs font-medium text-neutral-600">{label}</CardTitle>
        <Icon aria-hidden className="size-5 shrink-0 text-neutral-600" />
      </CardHeader>
      <CardContent className="flex flex-col gap-1 px-4">
        <p className="text-2xl font-bold text-black tabular-nums">{value}</p>
        {children}
      </CardContent>
    </Card>
  );
  return href ? (
    <Link href={href} className="block hover:[&>div]:border-black">
      {body}
    </Link>
  ) : (
    body
  );
}

/**
 * Bugün ekranı (Europe/Istanbul günü; tek uç: /api/dashboard/today). Personel kendi satışlarını ve kritik stoğu görür;
 * tahsilat, bakiye ve alacak kartları ona gelmez (API null döner). Randevu ve personel kartları ilgili sprintlerde.
 */
export function TodayDashboard() {
  const { role } = useSession();
  const [collecting, setCollecting] = useState(false);
  const data = useQuery({ queryKey: ["dashboard", "today"], queryFn: () => unwrap(api.GET("/api/dashboard/today")) });

  return (
    <>
      <QuickActions onCollect={() => setCollecting(true)} />
      {data.isPending ? (
        <p role="status" className="text-sm text-neutral-600">
          Yükleniyor…
        </p>
      ) : data.isError ? (
        <QueryError error={data.error} onRetry={data.refetch} />
      ) : (
        <DashboardBody data={data.data} />
      )}
      {collecting && can(role, "manageFinance") && <CollectionDialog open onOpenChange={(v) => !v && setCollecting(false)} />}
    </>
  );
}

function QuickActions({ onCollect }: { onCollect: () => void }) {
  const { role } = useSession();
  const sell = can(role, "createSales");
  const finance = can(role, "manageFinance");
  if (!sell && !finance) return null;
  return (
    <section aria-label="Hızlı işlemler" className="grid grid-cols-3 gap-2 sm:flex sm:flex-wrap">
      {sell && (
        <Button asChild size="lg">
          <Link href="/panel/satislar/yeni">
            <ReceiptText aria-hidden />
            Satış yap
          </Link>
        </Button>
      )}
      {finance && (
        <Button size="lg" variant="outline" onClick={onCollect}>
          <HandCoins aria-hidden />
          Tahsilat al
        </Button>
      )}
      {finance && (
        <Button asChild size="lg" variant="outline">
          <Link href="/panel/cari-kasa/gelir-gider/yeni?tur=gider">
            <Wallet aria-hidden />
            Gider ekle
          </Link>
        </Button>
      )}
    </section>
  );
}

function DashboardBody({ data }: { data: Dashboard }) {
  const { sales, receivables, stock } = data;
  const dueTotal = receivables ? receivables.dueToday + receivables.overdue : 0;

  return (
    <>
      <section aria-label="Özet" className="grid grid-cols-2 gap-3 lg:grid-cols-4 lg:gap-4">
        <StatCard label={sales.onlyOwn ? "Bugünkü satışlarım" : "Bugünkü satış"} icon={ReceiptText} value={formatMoney(sales.total)} href="/panel/satislar">
          <span className="text-xs text-neutral-600">{sales.count} satış</span>
          {sales.byMethod.map((m) => (
            <span key={m.method} className="text-xs text-neutral-700 tabular-nums">
              {paymentMethodNames[m.method]} {formatMoney(m.amount)}
            </span>
          ))}
        </StatCard>
        {data.collections !== null && (
          <StatCard label="Bugünkü tahsilat" icon={HandCoins} value={formatMoney(data.collections)} href="/panel/cari-kasa?sekme=bakiyeler" />
        )}
        {receivables && (
          <StatCard label="Vadesi gelen alacak" icon={AlertTriangle} value={formatMoney(dueTotal)} href="/panel/cari-kasa?sekme=vadeler">
            <span className="text-xs text-neutral-700 tabular-nums">
              Bugün {formatMoney(receivables.dueToday)} ({receivables.dueTodayContacts} müşteri)
            </span>
            <span className="text-xs text-neutral-700 tabular-nums">
              Gecikmiş {formatMoney(receivables.overdue)} ({receivables.overdueContacts} müşteri)
            </span>
          </StatCard>
        )}
        {stock.enabled && (
          <StatCard label="Kritik stok" icon={Boxes} value={`${stock.low} ürün`} href="/panel/stok?filtre=kritik">
            {stock.negative > 0 && <span className="text-xs text-neutral-700">{stock.negative} ürün eksi stokta</span>}
          </StatCard>
        )}
      </section>

      <div className="grid gap-4 lg:grid-cols-3">
        <Card className="lg:col-span-2">
          <CardHeader>
            <CardTitle className="text-black">{sales.onlyOwn ? "Son 30 gün · satışlarım" : "Son 30 gün satış"}</CardTitle>
          </CardHeader>
          <CardContent>
            <ChartContainer config={chartConfig} className="aspect-auto h-56 w-full" role="img" aria-label="Son 30 gün satış grafiği">
              <BarChart data={data.last30Days} margin={{ left: 0, right: 0, top: 8 }}>
                <CartesianGrid vertical={false} />
                <XAxis dataKey="date" tickLine={false} axisLine={false} tickMargin={6} minTickGap={16} tickFormatter={shortDay} />
                <YAxis tickLine={false} axisLine={false} width={44} tickFormatter={(v: number) => compactTl.format(v)} />
                <ChartTooltip
                  cursor={false}
                  content={
                    <ChartTooltipContent
                      labelFormatter={(value) => shortDay(String(value))}
                      formatter={(value) => <span className="font-semibold text-black tabular-nums">{formatMoney(Number(value))}</span>}
                    />
                  }
                />
                <Bar dataKey="total" fill="var(--color-total)" radius={2} />
              </BarChart>
            </ChartContainer>
          </CardContent>
        </Card>

        {data.accounts && (
          <Card>
            <CardHeader>
              <CardTitle className="text-black">Kasa ve banka</CardTitle>
            </CardHeader>
            <CardContent>
              <ul className="flex flex-col divide-y divide-border">
                {data.accounts.map((a) => (
                  <li key={a.id}>
                    <Link href={accountHref(a.id)} className="flex min-h-12 items-center gap-3 py-2">
                      {a.type === "Cash" ? <Wallet aria-hidden className="size-5 text-black" /> : <Landmark aria-hidden className="size-5 text-black" />}
                      <span className="min-w-0 flex-1 truncate text-sm text-black">{a.name}</span>
                      <AccountBalance value={a.balance} className="text-sm" />
                    </Link>
                  </li>
                ))}
              </ul>
            </CardContent>
          </Card>
        )}
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle className="text-black">Son satışlar</CardTitle>
          </CardHeader>
          <CardContent>
            {data.recentSales.length === 0 ? (
              <p className="text-sm text-neutral-600">Henüz satış yok.</p>
            ) : (
              <ul className="flex flex-col divide-y divide-border">
                {data.recentSales.map((s) => {
                  const when = istanbulParts(s.date);
                  return (
                    <li key={s.id}>
                      <Link href={`/panel/satislar/${s.id}`} className="flex min-h-12 items-center gap-3 py-2">
                        <span className="flex min-w-0 flex-1 flex-col">
                          <span className="truncate text-sm font-semibold text-black">{s.contactName ?? "Müşterisiz satış"}</span>
                          <span className="text-xs text-neutral-600 tabular-nums">
                            {s.no} · {when.date.split("-").reverse().join(".")} {when.time}
                            {s.status === "Cancelled" && " · İptal"}
                          </span>
                        </span>
                        <span className={`text-sm font-semibold tabular-nums ${s.status === "Cancelled" ? "text-neutral-600 line-through" : "text-black"}`}>
                          {formatMoney(s.total)}
                        </span>
                        <ChevronRight aria-hidden className="size-4 text-neutral-500" />
                      </Link>
                    </li>
                  );
                })}
              </ul>
            )}
          </CardContent>
        </Card>
        {stock.enabled && (
          <Card>
            <CardHeader>
              <CardTitle className="text-black">Kritik stok</CardTitle>
            </CardHeader>
            <CardContent>
              {stock.items.length === 0 ? (
                <p className="text-sm text-neutral-600">Kritik seviyede ürün yok.</p>
              ) : (
                <ul className="flex flex-col divide-y divide-border">
                  {stock.items.map((p) => (
                    <li key={p.id}>
                      <Link href={`/panel/urunler/${p.id}`} className="flex min-h-12 items-center gap-3 py-2">
                        <span className="min-w-0 flex-1 truncate text-sm text-black">{p.name}</span>
                        <span className="text-sm font-semibold text-black tabular-nums">
                          {p.quantity.toLocaleString("tr-TR")}
                          {p.criticalLevel !== null && <span className="font-normal text-neutral-600"> / {p.criticalLevel.toLocaleString("tr-TR")}</span>}
                        </span>
                      </Link>
                    </li>
                  ))}
                </ul>
              )}
            </CardContent>
          </Card>
        )}
      </div>
    </>
  );
}
