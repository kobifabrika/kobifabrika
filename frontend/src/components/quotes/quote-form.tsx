"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import { useRouter, useSearchParams } from "next/navigation";
import { useEffect, useReducer, useState } from "react";
import { toast } from "sonner";

import { ProductSearch } from "@/components/catalog/product-search";
import { QueryError } from "@/components/feedback/query-error";
import { Field } from "@/components/forms/field";
import { quotesKey, useQuotesRefresh } from "@/components/quotes/quote-bits";
import { CartEditor, DiscountInput, parseDiscount } from "@/components/sales/cart-editor";
import { ContactPicker } from "@/components/sales/contact-picker";
import { TotalsSummary } from "@/components/sales/totals";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { api, errorMessage, fieldErrors, unwrap } from "@/lib/api/client";
import type { Quote } from "@/lib/api/types";
import { cartReducer, cartTotals, emptyCart, toLineRequests, type Cart } from "@/lib/cart";
import { todayIstanbul } from "@/lib/format";
import type { Discount, DiscountType } from "@/lib/sales";

function addDays(isoDate: string, days: number) {
  const d = new Date(`${isoDate}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

/** Kayıtlı indirim girildiği şekliyle (TL/%); eski kayıtlarda tür yoksa TL karşılığı. */
function storedDiscount(spec: Discount | null, amount: number): Discount | null {
  return spec ?? (amount > 0 ? { type: "Amount", value: amount } : null);
}

/** Kayıtlı teklifi düzenlenebilir sepete çevirir; indirimler girildiği şekliyle (TL/%) açılır. */
function cartFromQuote(q: Quote): Cart {
  return {
    contact: { id: q.contactId, name: q.contactName, phone: q.contactPhone },
    discount: storedDiscount(q.discount, q.discountTotal),
    note: "",
    lines: q.lines.map((l) => ({
      key: l.id,
      productId: l.productId,
      name: l.description,
      unit: l.unit,
      unitPrice: l.unitPrice,
      vatRate: l.vatRate,
      quantity: l.quantity,
      discount: storedDiscount(l.discount, l.lineDiscount),
      trackStock: false,
      stock: null,
    })),
  };
}

/** Teklif hazırlama/düzenleme: müşteri (zorunlu), kalemler, indirim, tarih, geçerlilik (varsayılan 15 gün), not. */
export function QuoteForm({ quote }: { quote?: Quote }) {
  const router = useRouter();
  const params = useSearchParams();
  const refresh = useQuotesRefresh();
  const [cart, dispatch] = useReducer(cartReducer, quote ? cartFromQuote(quote) : emptyCart);
  const [date, setDate] = useState(quote?.date ?? todayIstanbul());
  const [validUntil, setValidUntil] = useState(quote?.validUntil ?? addDays(todayIstanbul(), 15));
  const [notes, setNotes] = useState(quote?.notes ?? "");
  const initialDiscount = quote ? storedDiscount(quote.discount, quote.discountTotal) : null;
  const [discountType, setDiscountType] = useState<DiscountType>(initialDiscount?.type ?? "Percent");
  const [discountText, setDiscountText] = useState(initialDiscount ? String(initialDiscount.value).replace(".", ",") : "");
  const [errors, setErrors] = useState<Record<string, string>>({});

  // Müşteri kartından "Teklif hazırla": ?musteri={id}
  const preset = params.get("musteri");
  const presetContact = useQuery({
    queryKey: ["contacts", "detail", preset],
    queryFn: () => unwrap(api.GET("/api/contacts/{id}", { params: { path: { id: preset! } } })),
    enabled: !!preset && !quote,
  });
  useEffect(() => {
    if (presetContact.data)
      dispatch({ type: "contact", contact: { id: presetContact.data.id, name: presetContact.data.displayName, phone: presetContact.data.phone } });
  }, [presetContact.data]);

  const discount = parseDiscount(discountType, discountText);
  const totals = cartTotals({ lines: cart.lines, discount: discount.discount });

  const save = useMutation({
    mutationFn: () => {
      const body = {
        contactId: cart.contact!.id,
        date,
        validUntil,
        lines: toLineRequests(cart.lines),
        discount: discount.discount,
        notes: notes.trim() || null,
      };
      return unwrap(
        quote ? api.PUT("/api/quotes/{id}", { params: { path: { id: quote.id } }, body }) : api.POST("/api/quotes", { body }),
      );
    },
    onSuccess: async (saved) => {
      await refresh();
      toast.success(quote ? "Teklif güncellendi." : `${saved.no} hazırlandı.`);
      router.push(`/panel/teklifler/${saved.id}`);
    },
    onError: (e) => {
      const fields = fieldErrors(e);
      if (Object.keys(fields).length) setErrors(fields);
      toast.error(errorMessage(e));
    },
  });

  const submit = () => {
    const next: Record<string, string> = {};
    if (!cart.contact) next.contactId = "Müşteri seçin.";
    if (cart.lines.length === 0) next.lines = "Teklife en az bir ürün veya hizmet ekleyin.";
    if (discount.error) next.discount = discount.error;
    if (validUntil < date) next.validUntil = "Geçerlilik tarihi teklif tarihinden önce olamaz.";
    setErrors(next);
    if (Object.keys(next).length === 0) save.mutate();
  };

  return (
    <div className="grid gap-6 lg:grid-cols-[1fr_22rem]">
      <div className="flex min-w-0 flex-col gap-4">
        <ContactPicker
          value={cart.contact}
          onChange={(c) => dispatch({ type: "contact", contact: c })}
          required
          error={errors.contactId}
        />
        <div className="flex flex-col gap-2">
          <span className="text-sm font-semibold text-black">Kalemler</span>
          <ProductSearch onPick={(p) => dispatch({ type: "add", product: p })} placeholder="Ürün/hizmet ekle: ad veya barkod" />
          {cart.lines.length > 0 && <CartEditor lines={cart.lines} calc={totals.lines} dispatch={dispatch} showStock={false} />}
          {errors.lines && (
            <p role="alert" className="text-sm text-danger-600">
              {errors.lines}
            </p>
          )}
        </div>
        <Field label="Not (teklifte görünür)" error={errors.notes}>
          <Textarea value={notes} onChange={(e) => setNotes(e.target.value)} maxLength={2000} rows={3} placeholder="ör. Montaj ve nakliye dahildir." />
        </Field>
      </div>

      <aside className="flex flex-col gap-4 lg:sticky lg:top-24 lg:self-start lg:border lg:border-border lg:p-4">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Tarih" error={errors.date}>
            <Input type="date" value={date} onChange={(e) => setDate(e.target.value)} />
          </Field>
          <Field label="Geçerlilik" error={errors.validUntil}>
            <Input type="date" value={validUntil} min={date} onChange={(e) => setValidUntil(e.target.value)} />
          </Field>
        </div>
        <DiscountInput
          label="Toplam indirim"
          type={discountType}
          text={discountText}
          onType={setDiscountType}
          onText={setDiscountText}
          error={errors.discount ?? discount.error}
        />
        <TotalsSummary totals={totals} large />
        <Button size="lg" onClick={submit} disabled={save.isPending}>
          {save.isPending ? "Kaydediliyor…" : quote ? "Teklifi kaydet" : "Teklifi hazırla"}
        </Button>
        <Button variant="outline" onClick={() => router.back()}>
          Vazgeç
        </Button>
      </aside>
    </div>
  );
}

export function EditQuote({ id }: { id: string }) {
  const quote = useQuery({
    queryKey: [...quotesKey, "detail", id],
    queryFn: () => unwrap(api.GET("/api/quotes/{id}", { params: { path: { id } } })),
  });
  if (quote.isPending)
    return (
      <p role="status" className="text-sm text-neutral-600">
        Yükleniyor…
      </p>
    );
  if (quote.isError) return <QueryError error={quote.error} onRetry={quote.refetch} />;
  if (quote.data.status !== "Draft" && quote.data.status !== "Sent")
    return <p className="text-sm text-neutral-700">Müşteri bu teklife cevap verdi; değiştirilemez. Gerekirse yeni teklif hazırlayın.</p>;
  return <QuoteForm quote={quote.data} />;
}
