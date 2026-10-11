"use client";

import { ArrowLeft, Printer } from "lucide-react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useEffect, useRef } from "react";

import { QueryError } from "@/components/feedback/query-error";
import { useSession } from "@/components/providers/session-provider";
import { useSale } from "@/components/sales/sale-detail";
import { Button } from "@/components/ui/button";
import { fileUrl } from "@/lib/api/client";
import { paymentMethodNames } from "@/lib/api/types";
import { displayPhone } from "@/lib/contacts";
import { formatMoney, istanbulParts } from "@/lib/format";
import { formatQuantity } from "@/lib/sales";

/** Fişin alt yazısı (backend Infrastructure/Pdf/DocumentPdf.cs → ReceiptFooter ile aynı). */
export const RECEIPT_FOOTER = "Bilgi fişidir, mali değeri yoktur.";

/**
 * 80 mm fiş: ekranda önizleme ve "Yazdır"; yazdırırken panel menüleri gizlenir, sayfa 80 mm genişlikte basılır.
 * `?yazdir=1` ile açılınca veri gelir gelmez yazdırma penceresi açılır.
 */
export function ReceiptView({ id }: { id: string }) {
  const { business, me } = useSession();
  const params = useSearchParams();
  const sale = useSale(id);
  const printed = useRef(false);

  useEffect(() => {
    if (sale.data && params.get("yazdir") === "1" && !printed.current) {
      printed.current = true;
      setTimeout(() => window.print(), 300);
    }
  }, [sale.data, params]);

  if (sale.isPending)
    return (
      <p role="status" className="text-sm text-neutral-600">
        Yükleniyor…
      </p>
    );
  if (sale.isError) return <QueryError error={sale.error} onRetry={sale.refetch} />;

  const s = sale.data;
  const { date, time } = istanbulParts(s.date);
  const logo = fileUrl(business.logoUrl);
  const address = [business.address, business.district, business.city].filter(Boolean).join(", ");
  const discount = s.lineDiscountTotal + s.discountTotal;

  return (
    <div className="flex flex-col items-center gap-4">
      <style>{"@media print { @page { size: 80mm auto; margin: 0; } body { background: #fff; } }"}</style>
      <div className="flex w-full max-w-[80mm] justify-between gap-2 print:hidden">
        <Button asChild variant="ghost">
          <Link href={`/panel/satislar/${s.id}`}>
            <ArrowLeft aria-hidden />
            Satış
          </Link>
        </Button>
        <Button onClick={() => window.print()}>
          <Printer aria-hidden />
          Yazdır
        </Button>
      </div>

      <article
        aria-label={`Fiş ${s.no}`}
        className="flex w-[80mm] max-w-full flex-col gap-1 border border-neutral-300 bg-white p-[4mm] font-mono text-[12px] leading-snug text-black print:border-0"
      >
        {s.status === "Cancelled" && <p className="border border-black py-0.5 text-center font-bold">İPTAL EDİLDİ</p>}
        {logo && (
          // eslint-disable-next-line @next/next/no-img-element -- logo API'den gelir; boyutu bilinmez.
          <img src={logo} alt="" className="mx-auto h-10 w-auto max-w-[50mm] object-contain grayscale" />
        )}
        <p className="text-center text-[14px] font-bold">{business.name}</p>
        {address && <p className="text-center">{address}</p>}
        {business.phone && <p className="text-center">{displayPhone(business.phone)}</p>}
        <hr className="my-1 border-black" />
        <p className="flex justify-between">
          <span className="font-bold">{s.no}</span>
          <span>
            {date.split("-").reverse().join(".")} {time}
          </span>
        </p>
        <p>Kasiyer: {s.createdByName ?? me.user.fullName}</p>
        {s.contactName && <p>Müşteri: {s.contactName}</p>}
        <hr className="my-1 border-black" />
        {s.lines.map((l) => (
          <div key={l.id}>
            <p className="font-bold">{l.description}</p>
            <p className="flex justify-between gap-2">
              <span>
                {formatQuantity(l.quantity, l.unit)} × {formatMoney(l.unitPrice)} KDV %{l.vatRate}
              </span>
              <span>{formatMoney(l.lineTotal + l.allocatedDiscount + l.lineDiscount)}</span>
            </p>
            {l.lineDiscount > 0 && <p>İndirim −{formatMoney(l.lineDiscount)}</p>}
          </div>
        ))}
        <hr className="my-1 border-black" />
        {discount > 0 && (
          <>
            <p className="flex justify-between">
              <span>Ara toplam</span>
              <span>{formatMoney(s.subTotal)}</span>
            </p>
            <p className="flex justify-between">
              <span>İndirim</span>
              <span>−{formatMoney(discount)}</span>
            </p>
          </>
        )}
        {s.vat
          .filter((v) => v.vat > 0)
          .map((v) => (
            <p key={v.rate} className="flex justify-between">
              <span>
                KDV %{v.rate} (matrah {formatMoney(v.base)})
              </span>
              <span>{formatMoney(v.vat)}</span>
            </p>
          ))}
        <p className="flex justify-between text-[15px] font-bold">
          <span>TOPLAM</span>
          <span>{formatMoney(s.total)}</span>
        </p>
        <hr className="my-1 border-dashed border-neutral-400" />
        {s.payments.map((p) => (
          <p key={p.id} className="flex justify-between">
            <span>{paymentMethodNames[p.method]}</span>
            <span>{formatMoney(p.amount)}</span>
          </p>
        ))}
        {s.note && <p className="pt-1">{s.note}</p>}
        <p className="pt-3 text-center font-bold">{RECEIPT_FOOTER}</p>
        <p className="text-center text-[12px]">KobiFabrika</p>
      </article>
    </div>
  );
}
