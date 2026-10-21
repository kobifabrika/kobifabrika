"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { toast } from "sonner";

import {
  ChoiceField,
  productsKey,
  useCatalogRefresh,
  useCategories,
  useStockModule,
  useVatRates,
} from "@/components/catalog/catalog-bits";
import { selectClass } from "@/components/contacts/contact-bits";
import { QueryError } from "@/components/feedback/query-error";
import { Field, FormMessage } from "@/components/forms/field";
import { useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { NumberInput } from "@/components/ui/number-input";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";
import { ApiError, api, errorMessage, fieldErrors, problemCode, unwrap } from "@/lib/api/client";
import type { Product, ProductRequest, ProductType, ProductUnit } from "@/lib/api/types";
import { formatMoney } from "@/lib/format";
import { can } from "@/lib/permissions";
import { addVat, parseQuantity, round2, unitNames, validateQuantity, vatOf } from "@/lib/sales";
import { moneyToInput, parseMoneyInput } from "@/lib/validation/rules";

type FormState = {
  type: ProductType;
  name: string;
  categoryId: string;
  unit: ProductUnit;
  vatRate: string;
  pricesIncludeVat: boolean;
  salePrice: string;
  purchasePrice: string;
  barcode: string;
  durationMinutes: string;
  trackStock: boolean;
  criticalStockLevel: string;
  initialStock: string;
  notes: string;
};

const quantityText = (v: number | null | undefined) => (v == null ? "" : String(v).replace(".", ","));

function fromProduct(p: Product | null, stockModule: boolean): FormState {
  return {
    type: p?.type ?? "Product",
    name: p?.name ?? "",
    categoryId: p?.categoryId ?? "",
    unit: p?.unit ?? "Piece",
    vatRate: String(p?.vatRate ?? 20),
    pricesIncludeVat: p?.pricesIncludeVat ?? true,
    salePrice: p ? moneyToInput(p.salePrice) : "",
    purchasePrice: p?.purchasePrice != null ? moneyToInput(p.purchasePrice) : "",
    barcode: p?.barcode ?? "",
    durationMinutes: p?.durationMinutes != null ? String(p.durationMinutes) : "",
    trackStock: p ? p.trackStock : stockModule,
    criticalStockLevel: quantityText(p?.criticalStockLevel),
    initialStock: "",
    notes: p?.notes ?? "",
  };
}

/** Fiyat: boş = yok; 0 olabilir; en fazla 2 ondalık. Hata mesajı veya değer. */
function parsePrice(text: string, required: boolean): { value: number | null; error?: string } {
  if (!text.trim()) return required ? { value: null, error: "Fiyat girin." } : { value: null };
  const parsed = parseMoneyInput(text);
  if (!parsed || parsed.value < 0) return { value: null, error: "Fiyatı rakamla girin (ör. 249,90)." };
  if (parsed.decimals > 2) return { value: null, error: "Fiyat en fazla 2 ondalık basamak içerebilir." };
  return { value: parsed.value };
}

/** Ürün/hizmet formu: yeni kayıt (product yok) veya düzenleme. Fiyatlar varsayılan KDV dahil girilir. */
export function ProductForm({ product }: { product?: Product }) {
  const router = useRouter();
  const { role } = useSession();
  const stockModule = useStockModule();
  const refresh = useCatalogRefresh();
  const vatRates = useVatRates();
  const categories = useCategories();
  const [form, setForm] = useState<FormState>(() => fromProduct(product ?? null, stockModule));
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [barcodeOwner, setBarcodeOwner] = useState<{ id: string; name: string } | null>(null);
  const isProduct = form.type === "Product";
  const showPurchase = can(role, "viewPurchasePrice");

  const set = <K extends keyof FormState>(key: K, value: FormState[K]) => {
    setForm((f) => ({ ...f, [key]: value }));
    setErrors((e) => ({ ...e, [key]: "" }));
  };

  const save = useMutation({
    mutationFn: (body: ProductRequest) =>
      unwrap(
        product
          ? api.PUT("/api/products/{id}", { params: { path: { id: product.id } }, body })
          : api.POST("/api/products", { body }),
      ),
    onSuccess: async (saved) => {
      await refresh();
      toast.success(product ? "Kaydedildi." : `${saved.name} eklendi.`);
      router.push(`/panel/urunler/${saved.id}`);
    },
    onError: (e) => {
      if (problemCode(e) === "barcode_taken" && e instanceof ApiError) {
        const p = e.problem as { productId?: string; productName?: string };
        setBarcodeOwner(p.productId ? { id: p.productId, name: p.productName ?? "" } : null);
        setErrors({ barcode: e.problem.detail ?? "Bu barkod kayıtlı." });
        return;
      }
      const fields = fieldErrors(e);
      if (Object.keys(fields).length) setErrors(fields);
      else toast.error(errorMessage(e));
    },
  });

  const rate = Number(form.vatRate);
  const price = parsePrice(form.salePrice, true).value;
  const preview =
    price != null && price > 0
      ? form.pricesIncludeVat
        ? `KDV hariç ${formatMoney(round2(price - vatOf(price, rate)))} · KDV ${formatMoney(vatOf(price, rate))}`
        : `KDV dahil ${formatMoney(addVat(price, rate))} · KDV ${formatMoney(round2(addVat(price, rate) - price))}`
      : null;

  const submit = (e: FormEvent) => {
    e.preventDefault();
    setBarcodeOwner(null);
    const next: Record<string, string> = {};
    if (!form.name.trim()) next.name = "Ad girin.";
    const sale = parsePrice(form.salePrice, true);
    if (sale.error) next.salePrice = sale.error;
    const purchase = parsePrice(form.purchasePrice, false);
    if (purchase.error) next.purchasePrice = purchase.error;
    const trackStock = isProduct && stockModule && form.trackStock;
    const critical = form.criticalStockLevel.trim() ? parseQuantity(form.criticalStockLevel) : null;
    if (trackStock && form.criticalStockLevel.trim()) {
      const error = validateQuantity(critical, form.unit, true);
      if (error) next.criticalStockLevel = error;
    }
    const initial = form.initialStock.trim() ? parseQuantity(form.initialStock) : null;
    if (trackStock && !product && form.initialStock.trim()) {
      const error = validateQuantity(initial, form.unit, true);
      if (error) next.initialStock = error;
    }
    const duration = form.durationMinutes.trim() ? Number(form.durationMinutes) : null;
    if (!isProduct && duration !== null && (!Number.isInteger(duration) || duration < 1 || duration > 1440))
      next.durationMinutes = "Süre 1 ile 1440 dakika arasında olmalı.";
    setErrors(next);
    if (Object.values(next).some(Boolean)) return;

    save.mutate({
      type: form.type,
      name: form.name.trim(),
      categoryId: form.categoryId || null,
      unit: form.unit,
      vatRate: rate,
      pricesIncludeVat: form.pricesIncludeVat,
      salePrice: sale.value,
      purchasePrice: showPurchase ? purchase.value : (product?.purchasePrice ?? null),
      barcode: form.barcode.trim() || null,
      durationMinutes: isProduct ? null : duration,
      trackStock,
      criticalStockLevel: trackStock ? critical : null,
      initialStock: trackStock && !product ? initial : null,
      notes: form.notes.trim() || null,
    });
  };

  if (vatRates.isError) return <QueryError error={vatRates.error} onRetry={vatRates.refetch} />;

  return (
    <form onSubmit={submit} className="flex max-w-2xl flex-col gap-5" noValidate>
      <ChoiceField
          label="Tür"
          value={form.type}
          onChange={(v) => {
            set("type", v);
            if (v === "Service" && form.unit === "Piece") set("unit", "Session");
            if (v === "Product" && (form.unit === "Session" || form.unit === "Hour")) set("unit", "Piece");
          }}
          options={[
            { value: "Product", label: "Ürün" },
            { value: "Service", label: "Hizmet" },
          ]}
        />

      <Field label="Ad" error={errors.name}>
        <Input value={form.name} onChange={(e) => set("name", e.target.value)} maxLength={200} autoComplete="off" />
      </Field>

      <div className="grid gap-5 sm:grid-cols-2">
        <Field
          label="Kategori"
          hint={
            categories.data?.length === 0 ? (
              <Link href="/panel/urunler?kategoriler=1" className="underline">
                Kategori ekle
              </Link>
            ) : undefined
          }
        >
          <select className={selectClass} value={form.categoryId} onChange={(e) => set("categoryId", e.target.value)}>
            <option value="">Kategorisiz</option>
            {categories.data?.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </Field>
        <Field label="Birim">
          <select className={selectClass} value={form.unit} onChange={(e) => set("unit", e.target.value as ProductUnit)}>
            {(Object.keys(unitNames) as ProductUnit[]).map((u) => (
              <option key={u} value={u}>
                {unitNames[u]}
              </option>
            ))}
          </select>
        </Field>
      </div>

      <ChoiceField
          label="KDV oranı"
          error={errors.vatRate}
          value={form.vatRate}
          onChange={(v) => set("vatRate", v)}
          options={(vatRates.data ?? [{ rate: 0, name: "%0" }, { rate: 1, name: "%1" }, { rate: 10, name: "%10" }, { rate: 20, name: "%20" }]).map(
            (r) => ({ value: String(r.rate), label: r.name }),
          )}
        />

      <div className="flex flex-col gap-3">
        <div className="grid gap-5 sm:grid-cols-2">
          <Field label="Satış fiyatı (TL)" error={errors.salePrice} hint={preview}>
            <NumberInput placeholder="0,00" value={form.salePrice} onChange={(e) => set("salePrice", e.target.value)} />
          </Field>
          {showPurchase && (
            <Field label="Alış fiyatı (TL)" error={errors.purchasePrice} hint="İsteğe bağlı. Personel göremez.">
              <NumberInput
                placeholder="0,00"
                value={form.purchasePrice}
                onChange={(e) => set("purchasePrice", e.target.value)}
              />
            </Field>
          )}
        </div>
        <label className="flex min-h-11 w-fit items-center gap-3 text-sm font-semibold text-black">
          <Switch checked={form.pricesIncludeVat} onCheckedChange={(v) => set("pricesIncludeVat", v)} />
          Fiyatlar KDV dahil
        </label>
      </div>

      <Field
        label="Barkod"
        error={errors.barcode}
        hint={barcodeOwner ? undefined : "İsteğe bağlı. Barkod okuyucuyla bu alana okutabilirsiniz."}
      >
        <Input
          value={form.barcode}
          onChange={(e) => set("barcode", e.target.value)}
          onKeyDown={(e) => e.key === "Enter" && e.preventDefault()}
          maxLength={64}
          autoComplete="off"
        />
      </Field>
      {barcodeOwner && (
        <Link href={`/panel/urunler/${barcodeOwner.id}`} className="-mt-3 w-fit text-sm font-semibold text-black underline">
          {barcodeOwner.name} kartına git
        </Link>
      )}

      {!isProduct && (
        <Field label="Süre (dakika)" error={errors.durationMinutes} hint="Randevu için hizmetin süresi.">
          <NumberInput kind="integer" value={form.durationMinutes} onChange={(e) => set("durationMinutes", e.target.value)} />
        </Field>
      )}

      {isProduct && stockModule && (
        <fieldset className="flex flex-col gap-4 border-t border-border pt-4">
          <legend className="sr-only">Stok</legend>
          <label className="flex min-h-11 w-fit items-center gap-3 text-sm font-semibold text-black">
            <Switch checked={form.trackStock} onCheckedChange={(v) => set("trackStock", v)} />
            Stok takibi
          </label>
          {form.trackStock && (
            <div className="grid gap-5 sm:grid-cols-2">
              {!product && (
                <Field label="Mevcut stok" error={errors.initialStock} hint="Açılış stoğu olarak girilir.">
                  <NumberInput value={form.initialStock} onChange={(e) => set("initialStock", e.target.value)} />
                </Field>
              )}
              <Field label="Kritik stok seviyesi" error={errors.criticalStockLevel} hint="Stok bu sayıya inince bildirim gelir.">
                <NumberInput
                  value={form.criticalStockLevel}
                  onChange={(e) => set("criticalStockLevel", e.target.value)}
                />
              </Field>
            </div>
          )}
        </fieldset>
      )}

      <Field label="Not" error={errors.notes}>
        <Textarea value={form.notes} onChange={(e) => set("notes", e.target.value)} maxLength={1000} rows={3} />
      </Field>

      {save.isError && !Object.values(errors).some(Boolean) && <FormMessage>{errorMessage(save.error)}</FormMessage>}

      <div className="flex flex-wrap gap-2">
        <Button type="submit" disabled={save.isPending}>
          {save.isPending ? "Kaydediliyor…" : product ? "Kaydet" : form.type === "Product" ? "Ürünü ekle" : "Hizmeti ekle"}
        </Button>
        <Button type="button" variant="outline" onClick={() => router.back()}>
          Vazgeç
        </Button>
      </div>
    </form>
  );
}

/** Düzenleme: ürünü yükler, formu doldurur. */
export function EditProduct({ id }: { id: string }) {
  const product = useQuery({
    queryKey: [...productsKey, "detail", id],
    queryFn: () => unwrap(api.GET("/api/products/{id}", { params: { path: { id } } })),
  });
  if (product.isPending)
    return (
      <p role="status" className="text-sm text-neutral-600">
        Yükleniyor…
      </p>
    );
  if (product.isError) return <QueryError error={product.error} onRetry={product.refetch} />;
  return <ProductForm product={product.data} />;
}
