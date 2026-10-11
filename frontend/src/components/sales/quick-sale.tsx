"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Check, FileDown, Plus, Printer, ReceiptText, ShoppingCart } from "lucide-react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useEffect, useReducer, useRef, useState } from "react";
import { toast } from "sonner";

import { productsKey, stockKey, useVatRates } from "@/components/catalog/catalog-bits";
import { ProductSearch, findByBarcode } from "@/components/catalog/product-search";
import { Notice } from "@/components/contacts/contact-bits";
import { Field } from "@/components/forms/field";
import { useSession } from "@/components/providers/session-provider";
import { CartEditor, DiscountInput, parseDiscount } from "@/components/sales/cart-editor";
import { ContactPicker } from "@/components/sales/contact-picker";
import { PaymentForm, initialPayment, paymentDrafts, type PaymentState } from "@/components/sales/payment-form";
import { TotalsSummary } from "@/components/sales/totals";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { API_URL, api, errorMessage, unwrap } from "@/lib/api/client";
import { paymentMethodNames, type Product, type Sale } from "@/lib/api/types";
import { useBarcodeScanner } from "@/lib/barcode";
import { cartReducer, cartTotals, emptyCart, loadDraft, paymentError, saveDraft, toLineRequests, type Cart } from "@/lib/cart";
import { formatMoney } from "@/lib/format";
import type { DiscountType } from "@/lib/sales";
import { parseMoneyInput } from "@/lib/validation/rules";

export const salesKey = ["sales"] as const;

/**
 * Hızlı satış (telefonda tek elle): ürün ara veya barkod okut → sepete ekle → adet/indirim → ödeme al.
 * Barkod okuyucu klavye gibi yazar; arama kutusunda veya sayfanın herhangi bir yerinde okutulan barkod ürünü direkt ekler.
 * Sepet taslağı bu tarayıcıda saklanır (sayfa yenilense de kaybolmaz).
 */
export function QuickSale() {
  const { business } = useSession();
  const queryClient = useQueryClient();
  const draftKey = `kf-cart-${business.id}`;
  // Taslak: bileşen sadece istemcide (oturum doğrulanınca) render edilir; ilk durum tarayıcıdaki taslaktan gelir.
  const [initialDraft] = useState(() => loadDraft(draftKey));
  const [cart, dispatch] = useReducer(cartReducer, initialDraft ?? emptyCart);
  const [payment, setPayment] = useState<PaymentState>(initialPayment());
  const [discountType, setDiscountType] = useState<DiscountType>(initialDraft?.discount?.type ?? "Percent");
  const [discountText, setDiscountText] = useState(
    initialDraft?.discount ? String(initialDraft.discount.value).replace(".", ",") : "",
  );
  const [checkoutOpen, setCheckoutOpen] = useState(false);
  const [freeOpen, setFreeOpen] = useState(false);
  const [done, setDone] = useState<{ sale: Sale; warnings: string[] } | null>(null);
  const [submitted, setSubmitted] = useState(false);
  const searchRef = useRef<HTMLInputElement>(null);

  useEffect(() => saveDraft(draftKey, cart), [cart, draftKey]);

  // Müşteri kartından "Satış yap": ?musteri={id}
  const preset = useSearchParams().get("musteri");
  const presetContact = useQuery({
    queryKey: ["contacts", "detail", preset],
    queryFn: () => unwrap(api.GET("/api/contacts/{id}", { params: { path: { id: preset! } } })),
    enabled: !!preset,
  });
  useEffect(() => {
    if (presetContact.data)
      dispatch({ type: "contact", contact: { id: presetContact.data.id, name: presetContact.data.displayName, phone: presetContact.data.phone } });
  }, [presetContact.data]);

  // Masaüstünde arama kutusu hazır odakta (telefonda klavye kendiliğinden açılmasın).
  useEffect(() => {
    if (window.matchMedia("(min-width: 1024px)").matches) searchRef.current?.focus();
  }, [done]);

  const topSelling = useQuery({
    queryKey: [...productsKey, "top-selling"],
    queryFn: () => unwrap(api.GET("/api/products/top-selling")),
  });

  const add = (product: Product) => {
    dispatch({ type: "add", product });
    toast.success(`${product.name} eklendi`, { duration: 1200 });
  };
  const notFound = (code: string) =>
    toast.error(`${code} barkodlu ürün yok.`, {
      action: { label: "Ürün ekle", onClick: () => window.open("/panel/urunler/yeni", "_blank") },
    });

  useBarcodeScanner(async (code) => {
    try {
      const product = await findByBarcode(code);
      if (product?.isActive) add(product);
      else notFound(code);
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }, !done);

  const discount = parseDiscount(discountType, discountText);
  useEffect(() => {
    if (!discount.error) dispatch({ type: "discount", discount: discount.discount });
    // discount nesnesi her render yenilenir; metin ve tip değişince uygula.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [discountType, discountText]);

  const totals = cartTotals(cart);
  const drafts = paymentDrafts(payment, totals.total);
  const problem = cart.lines.length === 0 ? "Sepet boş." : (discount.error ?? paymentError(totals.total, drafts, !!cart.contact));

  const submit = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/sales", {
          body: {
            contactId: cart.contact?.id ?? null,
            date: null,
            lines: toLineRequests(cart.lines),
            discount: cart.discount,
            payments: drafts.map((p) => ({ method: p.method, amount: p.amount, cashAccountId: null })),
            note: cart.note.trim() || null,
          },
        }),
      ),
    onSuccess: async (result) => {
      setDone(result);
      setCheckoutOpen(false);
      dispatch({ type: "clear" });
      setPayment(initialPayment());
      setDiscountText("");
      setSubmitted(false);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: salesKey }),
        queryClient.invalidateQueries({ queryKey: productsKey }),
        queryClient.invalidateQueries({ queryKey: stockKey }),
        queryClient.invalidateQueries({ queryKey: ["contacts"] }),
        queryClient.invalidateQueries({ queryKey: ["cash-accounts"] }),
      ]);
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  const complete = () => {
    setSubmitted(true);
    if (!problem) submit.mutate();
  };

  if (done) return <SaleDone result={done} onNew={() => setDone(null)} />;

  const checkout = (
    <Checkout
      cart={cart}
      onContact={(contact) => dispatch({ type: "contact", contact })}
      discountType={discountType}
      discountText={discountText}
      onDiscountType={setDiscountType}
      onDiscountText={setDiscountText}
      discountError={discount.error}
      totals={totals}
      payment={payment}
      onPayment={setPayment}
      problem={submitted ? problem : null}
      creditWithoutContact={drafts.some((p) => p.method === "Credit") && !cart.contact}
      pending={submit.isPending}
      onComplete={complete}
    />
  );

  return (
    <div className="grid gap-6 lg:grid-cols-[1fr_24rem]">
      <div className="flex min-w-0 flex-col gap-4">
        <div className="flex items-center justify-between gap-2">
          <h1 className="text-2xl font-bold tracking-tight text-black">Hızlı satış</h1>
          {cart.lines.length > 0 && (
            <Button variant="ghost" onClick={() => dispatch({ type: "clear" })}>
              Sepeti boşalt
            </Button>
          )}
        </div>
        <ProductSearch ref={searchRef} onPick={add} onNotFound={notFound} placeholder="Ürün adı veya barkod okutun" />

        {cart.lines.length === 0 ? (
          <div className="flex flex-col gap-3">
            {(topSelling.data?.length ?? 0) > 0 && (
              <>
                <span className="text-sm font-semibold text-black">Sık satılanlar</span>
                <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
                  {topSelling.data!.map((p) => (
                    <button
                      key={p.id}
                      type="button"
                      onClick={() => add(p)}
                      className="flex min-h-16 flex-col items-start justify-center rounded-lg border border-border px-3 py-2 text-left hover:border-black"
                    >
                      <span className="line-clamp-2 text-sm font-semibold text-black">{p.name}</span>
                      <span className="text-xs text-neutral-700 tabular-nums">{formatMoney(p.grossSalePrice)}</span>
                    </button>
                  ))}
                </div>
              </>
            )}
            <p className="flex items-center gap-2 text-sm text-neutral-600">
              <ShoppingCart aria-hidden className="size-4" />
              Sepet boş. Ürün arayın veya barkod okutun.
            </p>
          </div>
        ) : (
          <CartEditor lines={cart.lines} calc={totals.lines} dispatch={dispatch} />
        )}

        <Button variant="outline" className="self-start" onClick={() => setFreeOpen(true)}>
          <Plus aria-hidden />
          Serbest kalem
        </Button>
      </div>

      {/* Masaüstü: sağda ödeme paneli. */}
      <aside className="hidden lg:block">
        <div className="sticky top-24 flex flex-col gap-4 border border-border p-4">{checkout}</div>
      </aside>

      {/* Telefon: altta sabit toplam + "Ödeme al" (alt menünün üstünde). */}
      <div className="fixed inset-x-0 bottom-[calc(3.5rem+env(safe-area-inset-bottom))] z-20 border-t border-border bg-white px-4 py-3 lg:hidden">
        <div className="mx-auto flex max-w-xl items-center justify-between gap-3">
          <span className="flex flex-col">
            <span className="text-xs text-neutral-600">{cart.lines.length} kalem</span>
            <span className="text-xl font-bold text-black tabular-nums">{formatMoney(totals.total)}</span>
          </span>
          <Button size="lg" disabled={cart.lines.length === 0} onClick={() => setCheckoutOpen(true)}>
            Ödeme al
          </Button>
        </div>
      </div>
      <Sheet open={checkoutOpen} onOpenChange={setCheckoutOpen}>
        <SheetContent side="bottom" className="max-h-[92vh] gap-0 overflow-y-auto rounded-t-xl pb-[calc(1rem+env(safe-area-inset-bottom))] lg:hidden">
          <SheetHeader>
            <SheetTitle>Ödeme</SheetTitle>
            <SheetDescription>{cart.lines.length} kalem</SheetDescription>
          </SheetHeader>
          <div className="flex flex-col gap-4 px-4">{checkout}</div>
        </SheetContent>
      </Sheet>

      {freeOpen && <FreeLineDialog onClose={() => setFreeOpen(false)} onAdd={(l) => dispatch({ type: "addFree", ...l })} />}
    </div>
  );
}

function Checkout({
  cart,
  onContact,
  discountType,
  discountText,
  onDiscountType,
  onDiscountText,
  discountError,
  totals,
  payment,
  onPayment,
  problem,
  creditWithoutContact,
  pending,
  onComplete,
}: {
  cart: Cart;
  onContact: (c: Cart["contact"]) => void;
  discountType: DiscountType;
  discountText: string;
  onDiscountType: (t: DiscountType) => void;
  onDiscountText: (t: string) => void;
  discountError?: string;
  totals: ReturnType<typeof cartTotals>;
  payment: PaymentState;
  onPayment: (p: PaymentState) => void;
  problem: string | null;
  creditWithoutContact: boolean;
  pending: boolean;
  onComplete: () => void;
}) {
  return (
    <>
      <ContactPicker value={cart.contact} onChange={onContact} error={creditWithoutContact ? "Veresiye için müşteri seçin." : null} />
      <DiscountInput
        label="Toplam indirim"
        type={discountType}
        text={discountText}
        onType={onDiscountType}
        onText={onDiscountText}
        error={discountError}
      />
      <TotalsSummary totals={totals} large />
      <PaymentForm total={totals.total} value={payment} onChange={onPayment} />
      {problem && (
        <p role="alert" className="text-sm text-danger-600">
          {problem}
        </p>
      )}
      <Button size="lg" className="w-full" onClick={onComplete} disabled={pending || cart.lines.length === 0}>
        <Check aria-hidden />
        {pending ? "Kaydediliyor…" : `Satışı tamamla · ${formatMoney(totals.total)}`}
      </Button>
    </>
  );
}

function FreeLineDialog({ onClose, onAdd }: { onClose: () => void; onAdd: (l: { name: string; unitPrice: number; vatRate: number }) => void }) {
  const vatRates = useVatRates();
  const [name, setName] = useState("");
  const [price, setPrice] = useState("");
  const [vat, setVat] = useState("20");
  const [errors, setErrors] = useState<Record<string, string>>({});
  return (
    <Dialog open onOpenChange={(v) => !v && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Serbest kalem</DialogTitle>
          <DialogDescription>Katalogda olmayan bir ürün veya hizmet (fiyat KDV dahil).</DialogDescription>
        </DialogHeader>
        <form
          className="flex flex-col gap-4"
          noValidate
          onSubmit={(e) => {
            e.preventDefault();
            const p = parseMoneyInput(price);
            const next: Record<string, string> = {};
            if (!name.trim()) next.name = "Kalem adı girin.";
            if (!p || p.value < 0 || p.decimals > 2) next.price = "Fiyatı rakamla girin (ör. 50,00).";
            setErrors(next);
            if (Object.keys(next).length) return;
            onAdd({ name: name.trim(), unitPrice: p!.value, vatRate: Number(vat) });
            onClose();
          }}
        >
          <Field label="Ad" error={errors.name}>
            <Input value={name} onChange={(e) => setName(e.target.value)} maxLength={200} autoFocus />
          </Field>
          <Field label="Fiyat (TL)" error={errors.price}>
            <Input inputMode="decimal" value={price} onChange={(e) => setPrice(e.target.value)} />
          </Field>
          <Field label="KDV oranı">
            <select className="h-11 rounded-lg border border-input px-3 text-base" value={vat} onChange={(e) => setVat(e.target.value)}>
              {(vatRates.data ?? []).map((r) => (
                <option key={r.rate} value={String(r.rate)}>
                  {r.name}
                </option>
              ))}
            </select>
          </Field>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose}>
              Vazgeç
            </Button>
            <Button type="submit">Sepete ekle</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

/** Satış kaydedildi: numara, ödemeler, uyarılar; fiş yazdır, PDF, yeni satış. */
function SaleDone({ result, onNew }: { result: { sale: Sale; warnings: string[] }; onNew: () => void }) {
  const { sale, warnings } = result;
  return (
    <div className="mx-auto flex max-w-xl flex-col gap-5">
      <section className="flex items-start gap-3 border-l-2 border-brand bg-neutral-50 px-4 py-4">
        <Check aria-hidden className="mt-1 size-6 shrink-0 text-black" />
        <div className="flex flex-col gap-1">
          <p className="text-lg font-bold text-black">{sale.no} kaydedildi</p>
          <p className="text-2xl font-bold text-black tabular-nums">{formatMoney(sale.total)}</p>
          <p className="text-sm text-neutral-700">
            {sale.payments.map((p) => `${paymentMethodNames[p.method]} ${formatMoney(p.amount)}`).join(" · ")}
            {sale.contactName && ` · ${sale.contactName}`}
          </p>
        </div>
      </section>
      {warnings.map((w) => (
        <Notice key={w}>{w}</Notice>
      ))}
      <div className="grid grid-cols-2 gap-2">
        <Button asChild variant="outline">
          <Link href={`/panel/satislar/${sale.id}/fis?yazdir=1`}>
            <Printer aria-hidden />
            Fiş yazdır
          </Link>
        </Button>
        <Button asChild variant="outline">
          <a href={`${API_URL}/api/sales/${sale.id}/receipt.pdf`} target="_blank" rel="noopener noreferrer">
            <FileDown aria-hidden />
            PDF
          </a>
        </Button>
        <Button asChild variant="outline">
          <Link href={`/panel/satislar/${sale.id}`}>
            <ReceiptText aria-hidden />
            Satışa git
          </Link>
        </Button>
        <Button onClick={onNew}>
          <Plus aria-hidden />
          Yeni satış
        </Button>
      </div>
    </div>
  );
}
