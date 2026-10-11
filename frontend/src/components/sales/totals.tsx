import { cn } from "cn";

import { formatMoney } from "@/lib/format";

type Totals = {
  subTotal: number;
  lineDiscountTotal: number;
  discountTotal: number;
  vatTotal: number;
  total: number;
  vat: { rate: number; base: number; vat: number }[];
};

/** Ara toplam, indirimler, oran bazında KDV ve genel toplam (satış, teklif, fiş, herkese açık teklif). */
export function TotalsSummary({ totals, className, large = false }: { totals: Totals; className?: string; large?: boolean }) {
  const discount = totals.lineDiscountTotal + totals.discountTotal;
  return (
    <dl className={cn("flex flex-col gap-1 text-sm", className)}>
      {discount > 0 && (
        <>
          <div className="flex justify-between">
            <dt className="text-neutral-700">Ara toplam</dt>
            <dd className="text-black tabular-nums">{formatMoney(totals.subTotal)}</dd>
          </div>
          <div className="flex justify-between">
            <dt className="text-neutral-700">İndirim</dt>
            <dd className="text-black tabular-nums">−{formatMoney(discount)}</dd>
          </div>
        </>
      )}
      {totals.vat
        .filter((v) => v.vat > 0)
        .map((v) => (
          <div key={v.rate} className="flex justify-between text-neutral-700">
            <dt>
              KDV %{v.rate} <span className="text-xs">(matrah {formatMoney(v.base)})</span>
            </dt>
            <dd className="tabular-nums">{formatMoney(v.vat)}</dd>
          </div>
        ))}
      <div className={cn("flex justify-between border-t border-black pt-2 font-bold text-black", large ? "text-2xl" : "text-lg")}>
        <dt>Toplam</dt>
        <dd className="tabular-nums">{formatMoney(totals.total)}</dd>
      </div>
    </dl>
  );
}
