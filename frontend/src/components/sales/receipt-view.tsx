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
 * Fiş (Ayarlar → Fiş yazıcısı: 58 veya 80 mm): ekranda önizleme ve "Yazdır"; yazdırırken panel menüleri gizlenir, sayfa fiş
 * genişliğinde basılır. 58 mm'de uzun ürün adı ve tutar satırları alt satıra kayar, kesilmez.
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
  const narrow = business.receiptWidth === 58;
  // Tutar satırı: dar fişte etiket alt satıra kayabilir, tutar sağda kalır.
  const row = "flex flex-wrap justify-between gap-x-2";

  return (
    <div className="flex flex-col items-center gap-4">
      <style>{`@media print { @page { size: ${narrow ? 58 : 80}mm auto; margin: 0; } body { background: #fff; } }`}</style>
      <div className={`flex w-full justify-between gap-2 print:hidden ${narrow ? "max-w-[58mm]" : "max-w-[80mm]"}`}>
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
        data-width={narrow ? 58 : 80}
        className={`flex max-w-full flex-col gap-1 border border-neutral-300 bg-white font-mono text-[12px] leading-snug wrap-anywhere text-black print:border-0 ${
          narrow ? "w-[58mm] p-[3mm]" : "w-[80mm] p-[4mm]"
        }`}
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
        <p className={row}>
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
            <p className={row}>
              <span>
                {formatQuantity(l.quantity, l.unit)} × {formatMoney(l.unitPrice)} KDV %{l.vatRate}
              </span>
              <span className="ml-auto">{formatMoney(l.lineTotal + l.allocatedDiscount + l.lineDiscount)}</span>
            </p>
            {l.lineDiscount > 0 && <p>İndirim −{formatMoney(l.lineDiscount)}</p>}
          </div>
        ))}
        <hr className="my-1 border-black" />
        {discount > 0 && (
          <>
            <p className={row}>
              <span>Ara toplam</span>
              <span>{formatMoney(s.subTotal)}</span>
            </p>
            <p className={row}>
              <span>İndirim</span>
              <span>−{formatMoney(discount)}</span>
            </p>
          </>
        )}
        {s.vat
          .filter((v) => v.vat > 0)
          .map((v) => (
            <p key={v.rate} className={row}>
              <span>
                KDV %{v.rate} (matrah {formatMoney(v.base)})
              </span>
              <span className="ml-auto">{formatMoney(v.vat)}</span>
            </p>
          ))}
        <p className={`${row} font-bold ${narrow ? "text-[14px]" : "text-[15px]"}`}>
          <span>TOPLAM</span>
          <span>{formatMoney(s.total)}</span>
        </p>
        <hr className="my-1 border-dashed border-neutral-400" />
        {s.payments.map((p) => (
          <p key={p.id} className={row}>
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
