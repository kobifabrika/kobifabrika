"use client";

import { useQuery } from "@tanstack/react-query";
import { cn } from "cn";
import { Download } from "lucide-react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import type { ReactNode } from "react";

import { TabNav } from "@/components/contacts/contact-bits";
import { QueryError } from "@/components/feedback/query-error";
import { RangePicker, type RangeValue, resolveRange } from "@/components/finance/range-picker";
import { RequirePermission } from "@/components/panel/settings-nav";
import { useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { API_URL, api, unwrap } from "@/lib/api/client";
import type { IncomeExpenseReport, ReceivablesReport, ReportGroupBy, SalesReport, TopProductsReport } from "@/lib/api/types";
import { isPreset } from "@/lib/date-ranges";
import { formatDayShort, formatMoney } from "@/lib/format";
import { can } from "@/lib/permissions";
import { Chip, ScrollRow } from "@/components/ui/scroll-row";

const reports = [
  { key: "satislar", label: "Satışlar" },
  { key: "urunler", label: "En çok satan" },
  { key: "alacaklar", label: "Alacaklar" },
  { key: "gelir-gider", label: "Gelir-gider" },
] as const;

type ReportKey = (typeof reports)[number]["key"];

const groupKeys: Record<string, ReportGroupBy> = { gun: "Day", hafta: "Week", ay: "Month" };

/** URL'deki seçimler: ?rapor=…&aralik=bu-ay&from=…&to=…&kirilim=gun&siralama=adet (paylaşılabilir, geri tuşu çalışır). */
function useReportParams() {
  const params = useSearchParams();
  const router = useRouter();
  const pathname = usePathname();
  const key: ReportKey = reports.some((r) => r.key === params.get("rapor")) ? (params.get("rapor") as ReportKey) : "satislar";
  const preset = params.get("aralik");
  const range: RangeValue = {
    preset: isPreset(preset) ? preset : "bu-ay",
    from: params.get("from") ?? undefined,
    to: params.get("to") ?? undefined,
  };
  const set = (patch: Record<string, string | undefined>) => {
    const next = new URLSearchParams(params.toString());
    for (const [k, v] of Object.entries(patch)) {
      if (v) next.set(k, v);
      else next.delete(k);
    }
    router.replace(`${pathname}?${next.toString()}`, { scroll: false });
  };
  return { key, range, params, set };
}

export function Reports() {
  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-6">
      <h1 className="text-2xl font-bold tracking-tight text-black">Raporlar</h1>
      <RequirePermission permission="viewReports">
        <ReportsBody />
      </RequirePermission>
    </div>
  );
}

function ReportsBody() {
  const { role } = useSession();
  const { key, range, params, set } = useReportParams();
  const { from, to } = resolveRange(range);
  const groupBy = groupKeys[params.get("kirilim") ?? "gun"] ?? "Day";
  const sort = params.get("siralama") === "tutar" ? "Amount" : "Quantity";
  const query = key === "alacaklar" ? "" : `from=${from}&to=${to}`;
  const extra = key === "satislar" ? `&groupBy=${groupBy}` : key === "urunler" ? `&sort=${sort}` : "";
  const excelPath = { satislar: "sales", urunler: "top-products", alacaklar: "receivables", "gelir-gider": "income-expense" }[key];

  return (
    <div className="flex flex-col gap-5">
      <TabNav
        label="Raporlar"
        active={key}
        tabs={reports.map((r) => {
          const next = new URLSearchParams(params.toString());
          next.set("rapor", r.key);
          return { key: r.key, label: r.label, href: `?${next.toString()}` };
        })}
      />
      <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
        {key === "alacaklar" ? (
          <p className="text-sm text-neutral-600">Bugün itibarıyla açık alacaklar; vadeden bu yana geçen güne göre.</p>
        ) : (
          <RangePicker
            value={range}
            onChange={(v) => set({ aralik: v.preset, from: v.preset === "ozel" ? v.from : undefined, to: v.preset === "ozel" ? v.to : undefined })}
          />
        )}
        {can(role, "exportData") && (
          <Button variant="outline" asChild className="self-start">
            <a href={`${API_URL}/api/reports/${excelPath}.xlsx?${query}${extra}`} download>
              <Download aria-hidden />
              {"Excel'e aktar"}
            </a>
          </Button>
        )}
      </div>
      {key === "satislar" && <SalesReportView from={from} to={to} groupBy={groupBy} onGroup={(g) => set({ kirilim: g })} />}
      {key === "urunler" && <TopProductsView from={from} to={to} sort={sort} onSort={(s) => set({ siralama: s })} />}
      {key === "alacaklar" && <ReceivablesView />}
      {key === "gelir-gider" && <IncomeExpenseView from={from} to={to} />}
    </div>
  );
}

function Loading() {
  return (
    <p role="status" className="text-sm text-neutral-600">
      Yükleniyor…
    </p>
  );
}

function Toggle({ label, options, value, onChange }: { label: string; options: [string, string][]; value: string; onChange: (v: string) => void }) {
  return (
    <ScrollRow role="group" aria-label={label}>
      {options.map(([key, text]) => (
        <Chip key={key} active={value === key} onClick={() => onChange(key)}>
          {text}
        </Chip>
      ))}
    </ScrollRow>
  );
}

/** Masaüstü tablo; telefonda her satır başlıklı kart. */
function ResponsiveTable<T>({
  rows,
  columns,
  rowKey,
  total,
  title,
}: {
  rows: T[];
  columns: { label: string; value: (r: T) => ReactNode; numeric?: boolean }[];
  rowKey: (r: T) => string;
  total?: T;
  title: (r: T) => ReactNode;
}) {
  return (
    <>
      <div className="hidden overflow-x-auto md:block">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-black text-left text-neutral-700">
              {columns.map((c) => (
                <th key={c.label} className={cn("py-2 pr-3 font-semibold whitespace-nowrap", c.numeric && "text-right")}>
                  {c.label}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={rowKey(r)} className="border-b border-border">
                {columns.map((c) => (
                  <td key={c.label} className={cn("py-2 pr-3 text-black", c.numeric && "text-right tabular-nums")}>
                    {c.value(r)}
                  </td>
                ))}
              </tr>
            ))}
            {total && (
              <tr className="border-t border-black font-bold">
                {columns.map((c) => (
                  <td key={c.label} className={cn("py-2 pr-3 text-black", c.numeric && "text-right tabular-nums")}>
                    {c.value(total)}
                  </td>
                ))}
              </tr>
            )}
          </tbody>
        </table>
      </div>
      <ul className="flex flex-col divide-y divide-border border-y border-border md:hidden">
        {[...rows, ...(total ? [total] : [])].map((r, i) => (
          <li key={i === rows.length ? "toplam" : rowKey(r)} className="flex flex-col gap-1 py-3">
            <span className="text-sm font-semibold text-black">{title(r)}</span>
            <dl className="grid grid-cols-2 gap-x-3 gap-y-1 text-sm">
              {columns.slice(1).map((c) => (
                <div key={c.label} className="flex justify-between gap-2">
                  <dt className="text-neutral-600">{c.label}</dt>
                  <dd className="text-right text-black tabular-nums">{c.value(r)}</dd>
                </div>
              ))}
            </dl>
          </li>
        ))}
      </ul>
    </>
  );
}

function SalesReportView({ from, to, groupBy, onGroup }: { from: string; to: string; groupBy: ReportGroupBy; onGroup: (g: string) => void }) {
  const data = useQuery({
    queryKey: ["reports", "sales", from, to, groupBy],
    queryFn: () => unwrap(api.GET("/api/reports/sales", { params: { query: { from, to, groupBy } } })),
  });
  const groupKey = Object.entries(groupKeys).find(([, v]) => v === groupBy)?.[0] ?? "gun";
  type Row = SalesReport["rows"][number];
  return (
    <section aria-label="Satış raporu" className="flex flex-col gap-4">
      <Toggle label="Kırılım" options={[["gun", "Gün"], ["hafta", "Hafta"], ["ay", "Ay"]]} value={groupKey} onChange={onGroup} />
      {data.isPending ? (
        <Loading />
      ) : data.isError ? (
        <QueryError error={data.error} onRetry={data.refetch} />
      ) : data.data.rows.length === 0 ? (
        <p className="text-sm text-neutral-600">Bu aralıkta satış yok.</p>
      ) : (
        <ResponsiveTable<Row>
          rows={data.data.rows}
          total={data.data.totals}
          rowKey={(r) => r.periodStart}
          title={(r) => r.label}
          columns={[
            { label: "Dönem", value: (r) => r.label },
            { label: "Adet", value: (r) => r.count, numeric: true },
            { label: "Toplam", value: (r) => formatMoney(r.total), numeric: true },
            { label: "Nakit", value: (r) => formatMoney(r.cash), numeric: true },
            { label: "Kart", value: (r) => formatMoney(r.card), numeric: true },
            { label: "Havale/EFT", value: (r) => formatMoney(r.transfer), numeric: true },
            { label: "Veresiye", value: (r) => formatMoney(r.credit), numeric: true },
            { label: "İptal", value: (r) => (r.cancelledCount ? `${r.cancelledCount} · ${formatMoney(r.cancelledTotal)}` : "—"), numeric: true },
          ]}
        />
      )}
      <p className="text-xs text-neutral-600">Toplamlar KDV dahildir; iptal edilen satışlar toplama girmez.</p>
    </section>
  );
}

const kindNames = { Product: "Ürün", Service: "Hizmet", Free: "Serbest kalem" } as const;

function TopProductsView({ from, to, sort, onSort }: { from: string; to: string; sort: "Quantity" | "Amount"; onSort: (s: string) => void }) {
  const data = useQuery({
    queryKey: ["reports", "top-products", from, to, sort],
    queryFn: () => unwrap(api.GET("/api/reports/top-products", { params: { query: { from, to, sort, take: 50 } } })),
  });
  type Row = TopProductsReport["rows"][number];
  return (
    <section aria-label="En çok satan" className="flex flex-col gap-4">
      <Toggle label="Sıralama" options={[["adet", "Adede göre"], ["tutar", "Tutara göre"]]} value={sort === "Amount" ? "tutar" : "adet"} onChange={onSort} />
      {data.isPending ? (
        <Loading />
      ) : data.isError ? (
        <QueryError error={data.error} onRetry={data.refetch} />
      ) : data.data.rows.length === 0 ? (
        <p className="text-sm text-neutral-600">Bu aralıkta satış yok.</p>
      ) : (
        <ResponsiveTable<Row>
          rows={data.data.rows}
          rowKey={(r) => r.productId ?? r.name}
          title={(r) => `${data.data.rows.indexOf(r) + 1}. ${r.name}`}
          columns={[
            { label: "Ürün/hizmet", value: (r) => `${data.data.rows.indexOf(r) + 1}. ${r.name}` },
            { label: "Tür", value: (r) => kindNames[r.kind] },
            { label: "Miktar", value: (r) => r.quantity.toLocaleString("tr-TR"), numeric: true },
            { label: "Tutar", value: (r) => formatMoney(r.amount), numeric: true },
            { label: "Satış adedi", value: (r) => r.saleCount, numeric: true },
          ]}
        />
      )}
    </section>
  );
}

function ReceivablesView() {
  const data = useQuery({ queryKey: ["reports", "receivables"], queryFn: () => unwrap(api.GET("/api/reports/receivables")) });
  type Row = ReceivablesReport["rows"][number];
  const money = (v: number) => (v ? formatMoney(v) : "—");
  return (
    <section aria-label="Alacaklar" className="flex flex-col gap-4">
      {data.isPending ? (
        <Loading />
      ) : data.isError ? (
        <QueryError error={data.error} onRetry={data.refetch} />
      ) : data.data.rows.length === 0 ? (
        <p className="text-sm text-neutral-600">Açık alacak yok.</p>
      ) : (
        <ResponsiveTable<Row>
          rows={data.data.rows}
          total={data.data.totals}
          rowKey={(r) => r.contactId}
          title={(r) => r.name}
          columns={[
            { label: "Müşteri", value: (r) => r.name },
            { label: "Toplam", value: (r) => formatMoney(r.balance), numeric: true },
            { label: "Vadesi gelmemiş", value: (r) => money(r.notDue), numeric: true },
            { label: "0-30 gün", value: (r) => money(r.days0To30), numeric: true },
            { label: "31-60 gün", value: (r) => money(r.days31To60), numeric: true },
            { label: "61-90 gün", value: (r) => money(r.days61To90), numeric: true },
            { label: "90+ gün", value: (r) => money(r.days90Plus), numeric: true },
            { label: "En eski vade", value: (r) => (r.oldestDueDate ? formatDayShort(r.oldestDueDate) : "—"), numeric: true },
          ]}
        />
      )}
    </section>
  );
}

const monthLabel = (key: string) =>
  new Intl.DateTimeFormat("tr-TR", { month: "short", year: "numeric", timeZone: "UTC" }).format(new Date(`${key}-01T00:00:00Z`));

function IncomeExpenseView({ from, to }: { from: string; to: string }) {
  const data = useQuery({
    queryKey: ["reports", "income-expense", from, to],
    queryFn: () => unwrap(api.GET("/api/reports/income-expense", { params: { query: { from, to } } })),
  });
  if (data.isPending) return <Loading />;
  if (data.isError) return <QueryError error={data.error} onRetry={data.refetch} />;
  const r: IncomeExpenseReport = data.data;
  const line = (name: string, values: number[], total: number, bold = false) => (
    <tr key={name} className={cn("border-b border-border", bold && "font-bold")}>
      <th scope="row" className="sticky left-0 bg-white py-2 pr-3 text-left font-[inherit] text-black">
        {name}
      </th>
      {values.map((v, i) => (
        <td key={i} className="py-2 pr-3 text-right text-black tabular-nums">
          {v ? formatMoney(v) : "—"}
        </td>
      ))}
      <td className="py-2 text-right text-black tabular-nums">{formatMoney(total)}</td>
    </tr>
  );
  const sum = (xs: number[]) => xs.reduce((a, b) => a + b, 0);

  return (
    <section aria-label="Gelir-gider" className="flex flex-col gap-4">
      <dl className="grid grid-cols-3 gap-3 border-y border-border py-3 text-sm">
        {[
          ["Gelir", sum(r.incomeTotals)],
          ["Gider", sum(r.expenseTotals)],
          ["Net", sum(r.net)],
        ].map(([label, value]) => (
          <div key={label as string} className="flex flex-col">
            <dt className="text-neutral-600">{label}</dt>
            <dd className="text-base font-bold text-black tabular-nums">{formatMoney(value as number)}</dd>
          </div>
        ))}
      </dl>
      <div className="overflow-x-auto" tabIndex={0} role="region" aria-label="Aylık gelir-gider tablosu">
        <table className="w-full min-w-max text-sm">
          <thead>
            <tr className="border-b border-black text-neutral-700">
              <th className="sticky left-0 bg-white py-2 pr-3 text-left font-semibold">Kalem</th>
              {r.months.map((m) => (
                <th key={m} className="py-2 pr-3 text-right font-semibold whitespace-nowrap">
                  {monthLabel(m)}
                </th>
              ))}
              <th className="py-2 text-right font-semibold">Toplam</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <th colSpan={r.months.length + 2} className="pt-3 pb-1 text-left text-xs font-semibold tracking-wide text-neutral-600 uppercase">
                Gelir
              </th>
            </tr>
            {r.income.map((l) => line(l.name, l.values, l.total))}
            {line("Gelir toplamı", r.incomeTotals, sum(r.incomeTotals), true)}
            <tr>
              <th colSpan={r.months.length + 2} className="pt-4 pb-1 text-left text-xs font-semibold tracking-wide text-neutral-600 uppercase">
                Gider
              </th>
            </tr>
            {r.expense.map((l) => line(l.name, l.values, l.total))}
            {line("Gider toplamı", r.expenseTotals, sum(r.expenseTotals), true)}
            {line("Net (gelir − gider)", r.net, sum(r.net), true)}
          </tbody>
        </table>
      </div>
      <p className="text-xs text-neutral-600">
        {"Satışlar KDV dahil, satış tarihine göre (veresiye dahil, iptaller hariç). Ters kayıtlar yapıldığı ayda düşülür; transfer ve " +
          "tedarikçiye ödeme gelir-gidere girmez. Excel'de satışların KDV hariç ve KDV tutarları ayrıca yer alır."}
      </p>
    </section>
  );
}
