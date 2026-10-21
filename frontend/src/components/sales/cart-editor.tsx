"use client";

import { Minus, Plus, Trash2 } from "lucide-react";
import { useState, type Dispatch } from "react";

import { ChoiceGroup } from "@/components/catalog/catalog-bits";
import { Notice, selectClass } from "@/components/contacts/contact-bits";
import { Field } from "@/components/forms/field";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { NumberInput } from "@/components/ui/number-input";
import { stockWarning, type CartAction, type CartLine } from "@/lib/cart";
import { formatMoney } from "@/lib/format";
import { allowsFraction, formatQuantity, parseQuantity, validateQuantity, type CalcLine, type Discount, type DiscountType } from "@/lib/sales";
import { moneyToInput, parseMoneyInput } from "@/lib/validation/rules";
import { useIsDesktop } from "@/lib/use-media";

/** İndirim girişi: TL veya %; boş = indirim yok. Hata mesajı veya değer döner (validate). */
export function parseDiscount(type: DiscountType, text: string): { discount: Discount | null; error?: string } {
  if (!text.trim()) return { discount: null };
  const parsed = parseMoneyInput(text);
  if (!parsed || parsed.value < 0) return { discount: null, error: "İndirimi rakamla girin." };
  if (parsed.decimals > 2) return { discount: null, error: "En fazla 2 ondalık basamak." };
  if (type === "Percent" && parsed.value > 100) return { discount: null, error: "Yüzde en fazla 100 olabilir." };
  return { discount: parsed.value === 0 ? null : { type, value: parsed.value } };
}

export function DiscountInput({
  label,
  type,
  text,
  onType,
  onText,
  error,
}: {
  label: string;
  type: DiscountType;
  text: string;
  onType: (t: DiscountType) => void;
  onText: (t: string) => void;
  error?: string;
}) {
  return (
    <div className="flex flex-col gap-2">
      <span className="text-sm font-semibold text-black">{label}</span>
      <div className="flex gap-2">
        <ChoiceGroup
          label={`${label} tipi`}
          value={type}
          onChange={onType}
          className="shrink-0 flex-nowrap"
          options={[
            { value: "Amount", label: "TL" },
            { value: "Percent", label: "%" },
          ]}
        />
        <NumberInput
          value={text}
          onChange={(e) => onText(e.target.value)}
          placeholder={type === "Percent" ? "ör. 10" : "ör. 25,00"}
          aria-label={`${label} (${type === "Percent" ? "yüzde" : "TL"})`}
          aria-invalid={error ? true : undefined}
        />
      </div>
      {error && (
        <p role="alert" className="text-sm text-danger-600">
          {error}
        </p>
      )}
    </div>
  );
}

/** Satır personeli seçeneği (aktif personel). */
export type StaffOption = { id: string; fullName: string };

/**
 * Sepet satırları: ad, birim fiyat, − adet + (44px), satır tutarı. Satıra dokununca adet, fiyat, satır indirimi ve (satışta)
 * satır personeli düzenlenir; personel satırda küçük gri yazar. Stok yetersizse satır altında uyarı (satışı engellemez).
 */
export function CartEditor({
  lines,
  calc,
  dispatch,
  showStock = true,
  staff,
}: {
  lines: CartLine[];
  calc: CalcLine[];
  dispatch: Dispatch<CartAction>;
  /** Teklifte stok uyarısı gösterilmez (stok satışta düşer). */
  showStock?: boolean;
  /** Satışta satır personeli seçilir (prim); teklifte yok. */
  staff?: StaffOption[];
}) {
  const [editing, setEditing] = useState<CartLine | null>(null);
  return (
    <>
      <ul className="flex flex-col divide-y divide-border border-y border-border" aria-label="Sepet">
        {lines.map((line, i) => {
          const warning = showStock ? stockWarning(line) : null;
          const c = calc[i];
          return (
            <li key={line.key} className="flex flex-col gap-1 py-3">
              <div className="flex items-center gap-2">
                <button type="button" onClick={() => setEditing(line)} className="flex min-h-11 min-w-0 flex-1 flex-col items-start text-left">
                  <span className="w-full truncate text-sm font-semibold text-black">{line.name}</span>
                  <span className="text-xs text-neutral-700 tabular-nums">
                    {formatMoney(line.unitPrice)}
                    {line.unit !== "Piece" && ` / ${formatQuantity(1, line.unit).replace(/^1 /, "")}`}
                    {line.discount && ` · indirim ${line.discount.type === "Percent" ? `%${line.discount.value}` : formatMoney(line.discount.value)}`}
                  </span>
                  {staff && line.staffName && <span className="w-full truncate text-xs text-neutral-700">{line.staffName}</span>}
                </button>
                <div className="flex shrink-0 items-center">
                  <Button
                    type="button"
                    variant="outline"
                    size="icon"
                    aria-label={`${line.name}: azalt`}
                    onClick={() => dispatch({ type: "step", key: line.key, delta: -1 })}
                  >
                    {line.quantity <= 1 ? <Trash2 aria-hidden /> : <Minus aria-hidden />}
                  </Button>
                  <button
                    type="button"
                    onClick={() => setEditing(line)}
                    className="min-h-11 min-w-12 px-1 text-center text-sm font-semibold text-black tabular-nums"
                    aria-label={`Miktar: ${formatQuantity(line.quantity, line.unit)}`}
                  >
                    {allowsFraction(line.unit) ? formatQuantity(line.quantity, line.unit) : line.quantity}
                  </button>
                  {!line.packageId && (
                    <Button
                      type="button"
                      variant="outline"
                      size="icon"
                      aria-label={`${line.name}: artır`}
                      onClick={() => dispatch({ type: "step", key: line.key, delta: 1 })}
                    >
                      <Plus aria-hidden />
                    </Button>
                  )}
                </div>
                <span className="w-24 shrink-0 text-right text-sm font-semibold text-black tabular-nums">
                  {formatMoney(c ? c.gross - c.lineDiscount : line.unitPrice * line.quantity)}
                </span>
              </div>
              {warning && <Notice className="text-xs">{warning}</Notice>}
            </li>
          );
        })}
      </ul>
      {editing && (
        <LineDialog
          key={editing.key}
          line={editing}
          staff={staff}
          onClose={() => setEditing(null)}
          onSave={(patch) => {
            dispatch({ type: "update", key: editing.key, patch });
            setEditing(null);
          }}
          onRemove={() => {
            dispatch({ type: "remove", key: editing.key });
            setEditing(null);
          }}
        />
      )}
    </>
  );
}

function LineDialog({
  line,
  staff,
  onClose,
  onSave,
  onRemove,
}: {
  line: CartLine;
  staff?: StaffOption[];
  onClose: () => void;
  onSave: (patch: Partial<Pick<CartLine, "quantity" | "unitPrice" | "discount" | "staffMemberId" | "staffName">>) => void;
  onRemove: () => void;
}) {
  // Telefonda pencere açılınca klavye açılıp düğmeleri örtmesin; masaüstünde ilk alana odaklanılır.
  const isDesktop = useIsDesktop();
  const [quantity, setQuantity] = useState(String(line.quantity).replace(".", ","));
  const [price, setPrice] = useState(moneyToInput(line.unitPrice));
  const [discountType, setDiscountType] = useState<DiscountType>(line.discount?.type ?? "Percent");
  const [discountText, setDiscountText] = useState(line.discount ? String(line.discount.value).replace(".", ",") : "");
  const [staffId, setStaffId] = useState(line.staffMemberId ?? "");
  const [errors, setErrors] = useState<Record<string, string>>({});
  // Pasife alınmış personel satırda kalmışsa seçenek olarak gösterilir.
  const staffOptions =
    staff && line.staffMemberId && !staff.some((s) => s.id === line.staffMemberId)
      ? [...staff, { id: line.staffMemberId, fullName: line.staffName ?? "Personel" }]
      : staff;

  const save = () => {
    const q = parseQuantity(quantity);
    const next: Record<string, string> = {};
    const qError = validateQuantity(q, line.unit);
    if (qError) next.quantity = qError;
    const p = parseMoneyInput(price);
    if (!p || p.value < 0 || p.decimals > 2) next.price = "Fiyatı rakamla girin (ör. 249,90).";
    const d = parseDiscount(discountType, discountText);
    if (d.error) next.discount = d.error;
    setErrors(next);
    if (Object.keys(next).length) return;
    const staffPatch = staffOptions
      ? { staffMemberId: staffId || null, staffName: staffOptions.find((s) => s.id === staffId)?.fullName ?? null }
      : {};
    onSave({ quantity: q!, unitPrice: p!.value, discount: d.discount, ...staffPatch });
  };

  return (
    <Dialog open onOpenChange={(v) => !v && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{line.name}</DialogTitle>
          <DialogDescription>Fiyatlar KDV dahil.</DialogDescription>
        </DialogHeader>
        <form
          className="flex flex-col gap-4"
          onSubmit={(e) => {
            e.preventDefault();
            save();
          }}
          noValidate
        >
          <Field label={`Miktar (${formatQuantity(1, line.unit).replace(/^1 /, "")})`} error={errors.quantity}>
            <NumberInput value={quantity} onChange={(e) => setQuantity(e.target.value)} autoFocus={isDesktop} />
          </Field>
          <Field label="Birim fiyat (TL)" error={errors.price}>
            <NumberInput value={price} onChange={(e) => setPrice(e.target.value)} />
          </Field>
          <DiscountInput
            label="Satır indirimi"
            type={discountType}
            text={discountText}
            onType={setDiscountType}
            onText={setDiscountText}
            error={errors.discount}
          />
          {staffOptions && staffOptions.length > 0 && (
            <Field label="Personel" hint="Prim bu personele yazılır.">
              <select className={selectClass} value={staffId} onChange={(e) => setStaffId(e.target.value)}>
                <option value="">Personel yok</option>
                {staffOptions.map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.fullName}
                  </option>
                ))}
              </select>
            </Field>
          )}
          <DialogFooter className="flex-row justify-between gap-2 sm:justify-between">
            <Button type="button" variant="destructive" onClick={onRemove}>
              <Trash2 aria-hidden />
              Sil
            </Button>
            <Button type="submit">Tamam</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
