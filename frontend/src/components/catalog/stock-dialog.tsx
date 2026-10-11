"use client";

import { useMutation } from "@tanstack/react-query";
import { useState, type FormEvent } from "react";
import { toast } from "sonner";

import { ChoiceField, StockBadge, useCatalogRefresh } from "@/components/catalog/catalog-bits";
import { ProductSearch } from "@/components/catalog/product-search";
import { Field } from "@/components/forms/field";
import { useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { api, errorMessage, fieldErrors, unwrap } from "@/lib/api/client";
import type { Product } from "@/lib/api/types";
import { todayIstanbul } from "@/lib/format";
import { can } from "@/lib/permissions";
import { formatQuantity, parseQuantity, validateQuantity } from "@/lib/sales";
import { parseMoneyInput } from "@/lib/validation/rules";

type Kind = "In" | "Out" | "Adjustment";

/**
 * Stok girişi, çıkışı veya sayım düzeltmesi. Ürün verilmezse önce ürün aranır (barkod okutulabilir).
 * Sayımda sayılan miktar girilir; fark sunucuda hesaplanır. Eksi stoğa izin var, uyarı gösterilir.
 */
export function StockDialog({
  product: initial,
  kind: initialKind = "In",
  open,
  onOpenChange,
}: {
  product?: Product | null;
  kind?: Kind;
  open: boolean;
  onOpenChange: (v: boolean) => void;
}) {
  const { role } = useSession();
  const refresh = useCatalogRefresh();
  const [product, setProduct] = useState<Product | null>(initial ?? null);
  const [kind, setKind] = useState<Kind>(initialKind);
  const [quantity, setQuantity] = useState("");
  const [unitCost, setUnitCost] = useState("");
  const [date, setDate] = useState(todayIstanbul());
  const [note, setNote] = useState("");
  const [errors, setErrors] = useState<Record<string, string>>({});

  const save = useMutation({
    mutationFn: (body: { quantity: number; unitCost: number | null }) =>
      unwrap(
        api.POST("/api/stock/movements", {
          body: {
            productId: product!.id,
            type: kind,
            quantity: kind === "Adjustment" ? null : body.quantity,
            countedQuantity: kind === "Adjustment" ? body.quantity : null,
            unitCost: body.unitCost,
            date,
            note: note.trim() || null,
          },
        }),
      ),
    onSuccess: async (result) => {
      await refresh();
      for (const w of result.warnings) toast.warning(w);
      toast.success(
        result.movement
          ? `${result.product.name}: stok ${formatQuantity(result.product.stockQuantity, result.product.unit)}`
          : "Sayılan miktar stokla aynı; değişiklik yok.",
      );
      onOpenChange(false);
    },
    onError: (e) => {
      const fields = fieldErrors(e);
      if (Object.keys(fields).length) setErrors(fields);
      else toast.error(errorMessage(e));
    },
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (!product) return;
    const value = parseQuantity(quantity);
    const error = validateQuantity(value, product.unit, kind === "Adjustment");
    const cost = unitCost.trim() ? parseMoneyInput(unitCost) : null;
    const next: Record<string, string> = {};
    if (error) next.quantity = error;
    if (unitCost.trim() && (!cost || cost.value < 0 || cost.decimals > 2)) next.unitCost = "Maliyeti rakamla girin (ör. 62,50).";
    setErrors(next);
    if (Object.keys(next).length) return;
    save.mutate({ quantity: value!, unitCost: kind === "In" && cost ? cost.value : null });
  };

  const titles: Record<Kind, string> = { In: "Stok girişi", Out: "Stok çıkışı", Adjustment: "Sayım düzeltmesi" };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>{titles[kind]}</DialogTitle>
          <DialogDescription>
            {kind === "Adjustment" ? "Saydığınız miktarı girin; fark otomatik hesaplanır." : "Mal alımı, fire, iade gibi hareketler."}
          </DialogDescription>
        </DialogHeader>
        <form onSubmit={submit} className="flex flex-col gap-4" noValidate>
          {product ? (
            <div className="flex items-center justify-between gap-3 border border-border px-3 py-2">
              <span className="flex flex-col">
                <span className="text-sm font-semibold text-black">{product.name}</span>
                <StockBadge product={product} className="text-xs" />
              </span>
              {!initial && (
                <Button type="button" variant="ghost" onClick={() => setProduct(null)}>
                  Değiştir
                </Button>
              )}
            </div>
          ) : (
            <ProductSearch trackedOnly autoFocus onPick={setProduct} placeholder="Ürün adı veya barkod okutun" />
          )}

          <ChoiceField
            label="Hareket"
            value={kind}
            onChange={setKind}
            options={[
              { value: "In", label: "Giriş" },
              { value: "Out", label: "Çıkış" },
              { value: "Adjustment", label: "Sayım" },
            ]}
          />

          <Field label={kind === "Adjustment" ? "Sayılan miktar" : "Miktar"} error={errors.quantity}>
            <Input inputMode="decimal" value={quantity} onChange={(e) => setQuantity(e.target.value)} />
          </Field>
          {kind === "In" && can(role, "viewPurchasePrice") && (
            <Field label="Birim maliyet (TL)" error={errors.unitCost} hint="İsteğe bağlı.">
              <Input inputMode="decimal" value={unitCost} onChange={(e) => setUnitCost(e.target.value)} />
            </Field>
          )}
          <Field label="Tarih" error={errors.date}>
            <Input type="date" value={date} max={todayIstanbul()} onChange={(e) => setDate(e.target.value)} />
          </Field>
          <Field label="Not" error={errors.note}>
            <Input value={note} onChange={(e) => setNote(e.target.value)} maxLength={500} placeholder="ör. Toptancı faturası" />
          </Field>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Vazgeç
            </Button>
            <Button type="submit" disabled={!product || save.isPending}>
              {save.isPending ? "Kaydediliyor…" : "Kaydet"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
