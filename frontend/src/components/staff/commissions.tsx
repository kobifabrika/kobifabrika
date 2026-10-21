"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ChevronLeft, ChevronRight, Pencil, Percent, Plus, Trash2 } from "lucide-react";
import { useRouter, useSearchParams } from "next/navigation";
import { useState } from "react";
import { toast } from "sonner";

import { useCategories } from "@/components/catalog/catalog-bits";
import { ProductSearch } from "@/components/catalog/product-search";
import { EmptyState, Notice, selectClass } from "@/components/contacts/contact-bits";
import { QueryError } from "@/components/feedback/query-error";
import { Field, FormMessage } from "@/components/forms/field";
import { useSession } from "@/components/providers/session-provider";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { NumberInput } from "@/components/ui/number-input";
import { Switch } from "@/components/ui/switch";
import { api, errorMessage, fieldErrors, unwrap } from "@/lib/api/client";
import {
  commissionTargetNames,
  type CommissionItem,
  type CommissionMonth,
  type CommissionRule,
  type CommissionTarget,
  type CommissionType,
} from "@/lib/api/types";
import { formatDayShort, formatMoney, todayIstanbul } from "@/lib/format";
import { can } from "@/lib/permissions";
import { addMonths, monthLabel } from "@/lib/time-clock";
import { moneyToInput, parseMoneyInput } from "@/lib/validation/rules";

import { useStaffList } from "./staff-bits";

export const commissionsKey = ["commissions"] as const;

/** URL'deki ay (?ay=yyyy-MM; yoksa bu ay) ve değiştirici; bu ay seçilince parametre silinir. */
export function useMonthParam() {
  const router = useRouter();
  const params = useSearchParams();
  const current = todayIstanbul().slice(0, 7);
  const raw = params.get("ay") ?? "";
  const month = /^\d{4}-\d{2}$/.test(raw) ? raw : current;
  const go = (m: string) => {
    const next = new URLSearchParams(params.toString());
    if (m === current) next.delete("ay");
    else next.set("ay", m);
    router.replace(`?${next.toString()}`, { scroll: false });
  };
  return { month, go };
}

export function MonthStepper({ month, onChange }: { month: string; onChange: (m: string) => void }) {
  return (
    <div className="flex items-center gap-2">
      <Button variant="outline" size="icon" aria-label="Önceki ay" onClick={() => onChange(addMonths(month, -1))}>
        <ChevronLeft aria-hidden />
      </Button>
      <h2 className="min-w-36 text-center text-base font-semibold text-black">{monthLabel(month)}</h2>
      <Button variant="outline" size="icon" aria-label="Sonraki ay" onClick={() => onChange(addMonths(month, 1))}>
        <ChevronRight aria-hidden />
      </Button>
    </div>
  );
}

const quantity = (q: number) => q.toLocaleString("tr-TR", { maximumFractionDigits: 3 });

/**
 * Prim kalemleri: masaüstünde tablo, telefonda kart. İptal satırları eksi tutarla ve "İptal" etiketiyle (iptal gününde).
 * showStaff: personel sütunu (rapor); showEnteredBy: "Satışı giren" (rapor).
 */
export function CommissionItems({ items, showStaff, showEnteredBy }: { items: CommissionItem[]; showStaff?: boolean; showEnteredBy?: boolean }) {
  const kind = (i: CommissionItem) => (i.kind === "Reversal" ? <Badge variant="outline">İptal</Badge> : null);
  return (
    <>
      <div className="hidden overflow-x-auto md:block">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-black text-left text-neutral-700">
              {showStaff && <th className="py-2 pr-3 font-semibold">Personel</th>}
              <th className="py-2 pr-3 font-semibold">Gün</th>
              <th className="py-2 pr-3 font-semibold">Satış</th>
              <th className="py-2 pr-3 font-semibold">Kalem</th>
              <th className="py-2 pr-3 text-right font-semibold whitespace-nowrap">Taban (KDV hariç)</th>
              <th className="py-2 pr-3 font-semibold">Kural</th>
              {showEnteredBy && <th className="py-2 pr-3 font-semibold whitespace-nowrap">Satışı giren</th>}
              <th className="py-2 text-right font-semibold">Prim</th>
            </tr>
          </thead>
          <tbody>
            {items.map((i) => (
              <tr key={i.id} className="border-b border-border">
                {showStaff && <td className="py-2 pr-3 text-black">{i.staffName}</td>}
                <td className="py-2 pr-3 text-black tabular-nums">{formatDayShort(i.occurredOn)}</td>
                <td className="py-2 pr-3 whitespace-nowrap text-black tabular-nums">
                  {i.saleNo} {kind(i)}
                </td>
                <td className="py-2 pr-3 text-black">
                  {i.lineDescription}
                  {i.quantity !== 1 && <span className="text-neutral-600"> × {quantity(i.quantity)}</span>}
                </td>
                <td className="py-2 pr-3 text-right text-black tabular-nums">{formatMoney(i.base)}</td>
                <td className="py-2 pr-3 text-neutral-700">{i.ruleText}</td>
                {showEnteredBy && <td className="py-2 pr-3 text-black">{i.enteredByName ?? "—"}</td>}
                <td className="py-2 text-right font-semibold text-black tabular-nums">{formatMoney(i.amount)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <ul className="flex flex-col divide-y divide-border border-y border-border md:hidden">
        {items.map((i) => (
          <li key={i.id} className="flex flex-col gap-1 py-3">
            <div className="flex items-start justify-between gap-3">
              <span className="min-w-0 text-sm font-semibold text-black">
                {i.lineDescription}
                {i.quantity !== 1 && <span className="font-normal text-neutral-600"> × {quantity(i.quantity)}</span>}
              </span>
              <span className="shrink-0 text-sm font-semibold text-black tabular-nums">{formatMoney(i.amount)}</span>
            </div>
            <span className="flex flex-wrap items-center gap-x-2 text-xs text-neutral-700 tabular-nums">
              <span>{[showStaff ? i.staffName : null, formatDayShort(i.occurredOn), i.saleNo].filter(Boolean).join(" · ")}</span>
              {kind(i)}
            </span>
            <span className="text-xs text-neutral-700">
              Taban {formatMoney(i.base)} · {i.ruleText}
            </span>
            {showEnteredBy && i.enteredByName && <span className="text-xs text-neutral-700">Satışı giren: {i.enteredByName}</span>}
          </li>
        ))}
      </ul>
    </>
  );
}

/** "Primim" (Mesai ve İzin → Primim): kendi primi, ay seçici, toplam ve kalemler (karar b: sadece kendisininki). */
export function MyCommissions() {
  const { month, go } = useMonthParam();
  const data = useQuery({
    queryKey: [...commissionsKey, "me", month],
    queryFn: () => unwrap(api.GET("/api/commissions/me", { params: { query: { month } } })),
  });
  return (
    <section aria-label="Primim" className="flex flex-col gap-4">
      <MonthStepper month={month} onChange={go} />
      {data.isPending ? (
        <p role="status" className="text-sm text-neutral-600">
          Yükleniyor…
        </p>
      ) : data.isError ? (
        <QueryError error={data.error} onRetry={data.refetch} />
      ) : !data.data.staffMemberId ? (
        <Notice>Hesabınız bir personel kaydına bağlı değil; primler personel kaydına yazılır.</Notice>
      ) : (
        <MonthBody data={data.data} />
      )}
    </section>
  );
}

function MonthBody({ data }: { data: CommissionMonth }) {
  return (
    <>
      <div className="flex flex-col gap-1 border border-border p-4">
        <span className="text-sm text-neutral-600">{monthLabel(data.month)} primi</span>
        <span className="text-2xl font-bold text-black tabular-nums">{formatMoney(data.total)}</span>
        <span className="text-xs text-neutral-600">{data.items.length} kalem</span>
      </div>
      {data.items.length === 0 ? (
        <p className="text-sm text-neutral-600">Bu ay prim yok.</p>
      ) : (
        <CommissionItems items={data.items} />
      )}
      <p className="text-xs text-neutral-600">
        Prim, satış satırının KDV hariç ve indirim sonrası tutarından hesaplanır. İptal edilen satışın primi iptal edildiği ay eksi
        olarak düşer.
      </p>
    </>
  );
}

// ---- Prim kuralları ----

const targets: CommissionTarget[] = ["All", "Service", "Product", "Category", "Item"];

/**
 * Prim kuralları (Personel → Prim kuralları): kime (herkes/personel), neye (hepsi, hizmetler, ürünler, kategori, tek ürün),
 * yüzde veya adet başına TL. En özel kural kazanır; Mali Müşavir salt okunur görür.
 */
export function CommissionRules() {
  const { role } = useSession();
  const manage = can(role, "manageCommissions");
  const rules = useQuery({ queryKey: [...commissionsKey, "rules"], queryFn: () => unwrap(api.GET("/api/commission-rules")) });
  const [editing, setEditing] = useState<CommissionRule | "new" | null>(null);

  return (
    <section aria-label="Prim kuralları" className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="max-w-2xl text-sm text-neutral-600">
          Satış satırındaki personele prim yazılır. Taban, satırın KDV hariç ve indirim sonrası tutarıdır; sabit prim adet başınadır.
          Birden çok kural uyarsa en özel olanı geçerli olur (toplanmaz).
        </p>
        {manage && (
          <Button onClick={() => setEditing("new")}>
            <Plus aria-hidden />
            Kural ekle
          </Button>
        )}
      </div>
      <details className="max-w-2xl text-sm text-neutral-700">
        <summary className="flex min-h-11 cursor-pointer items-center font-semibold text-black">Öncelik sırası</summary>
        <ol className="list-decimal pl-5">
          <li>Personele özel, tek ürün/hizmet</li>
          <li>Herkes, tek ürün/hizmet</li>
          <li>Personele özel, kategori</li>
          <li>Herkes, kategori</li>
          <li>Personele özel, hizmetler veya ürünler</li>
          <li>Herkes, hizmetler veya ürünler</li>
          <li>Personele özel, bütün satışlar</li>
          <li>Herkes, bütün satışlar</li>
        </ol>
        <p className="mt-2">0 TL satırdan (paketten kullanılan seans dahil) prim çıkmaz; paketin primi paket satılırken yazılır.</p>
      </details>
      {rules.isPending ? (
        <p role="status" className="text-sm text-neutral-600">
          Yükleniyor…
        </p>
      ) : rules.isError ? (
        <QueryError error={rules.error} onRetry={rules.refetch} />
      ) : rules.data.length === 0 ? (
        <EmptyState icon={<Percent aria-hidden className="size-8" />} title="Henüz prim kuralı yok">
          <p className="text-sm text-neutral-600">Örnek: herkes için hizmetlerde %10, ürünlerde %5.</p>
        </EmptyState>
      ) : (
        <ul className="flex flex-col divide-y divide-border border-y border-border">
          {rules.data.map((r) => (
            <li key={r.id} className="flex min-h-14 items-center gap-3 py-2">
              <span className="flex min-w-0 flex-1 flex-col">
                <span className="text-sm font-semibold text-black">
                  {r.staffName ?? "Herkes"} · {ruleTarget(r)}
                </span>
                <span className="text-xs text-neutral-700">
                  {r.isActive ? `${r.rank}. öncelik` : "Pasif"}
                  {(r.target === "Category" || r.target === "Item") && ` · ${commissionTargetNames[r.target]}`}
                </span>
              </span>
              <span className="shrink-0 text-base font-bold text-black tabular-nums">{ruleRate(r.type, r.value)}</span>
              {manage && (
                <Button variant="ghost" size="icon" aria-label={`Düzenle: ${r.text}`} onClick={() => setEditing(r)}>
                  <Pencil aria-hidden />
                </Button>
              )}
            </li>
          ))}
        </ul>
      )}
      {editing && <RuleDialog rule={editing === "new" ? null : editing} onClose={() => setEditing(null)} />}
    </section>
  );
}

function ruleTarget(r: CommissionRule) {
  if (r.target === "Category") return r.categoryName ?? "(silinmiş kategori)";
  if (r.target === "Item") return r.productName ?? "(silinmiş ürün)";
  return commissionTargetNames[r.target];
}

export function ruleRate(type: CommissionType, value: number) {
  return type === "Percent" ? `%${value.toLocaleString("tr-TR", { maximumFractionDigits: 2 })}` : `${formatMoney(value)}/adet`;
}

function RuleDialog({ rule, onClose }: { rule: CommissionRule | null; onClose: () => void }) {
  const queryClient = useQueryClient();
  const staff = useStaffList();
  const categories = useCategories();
  const [staffId, setStaffId] = useState(rule?.staffMemberId ?? "");
  const [target, setTarget] = useState<CommissionTarget>(rule?.target ?? "Service");
  const [categoryId, setCategoryId] = useState(rule?.categoryId ?? "");
  const [product, setProduct] = useState<{ id: string; name: string } | null>(
    rule?.productId ? { id: rule.productId, name: rule.productName ?? "" } : null,
  );
  const [type, setType] = useState<CommissionType>(rule?.type ?? "Percent");
  const [value, setValue] = useState(rule ? (rule.type === "Percent" ? String(rule.value).replace(".", ",") : moneyToInput(rule.value)) : "");
  const [active, setActive] = useState(rule?.isActive ?? true);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [formError, setFormError] = useState<string | null>(null);

  const refresh = () => queryClient.invalidateQueries({ queryKey: commissionsKey });
  const save = useMutation({
    mutationFn: (body: Parameters<typeof saveRule>[1]) => saveRule(rule?.id ?? null, body),
    onSuccess: async () => {
      await refresh();
      toast.success(rule ? "Kural güncellendi" : "Kural eklendi");
      onClose();
    },
    onError: (e) => {
      const fields = fieldErrors(e);
      setErrors(fields);
      setFormError(Object.keys(fields).length ? null : errorMessage(e));
    },
  });
  const remove = useMutation({
    mutationFn: () => unwrap(api.DELETE("/api/commission-rules/{id}", { params: { path: { id: rule!.id } } })),
    onSuccess: async () => {
      await refresh();
      toast.success("Kural silindi");
      onClose();
    },
    onError: (e) => setFormError(errorMessage(e)),
  });

  const submit = () => {
    const next: Record<string, string> = {};
    const parsed = parseMoneyInput(value);
    if (!parsed || parsed.value <= 0) next.value = type === "Percent" ? "Oranı girin (ör. 10)." : "Tutarı girin (ör. 50,00).";
    else if (parsed.decimals > 2) next.value = "En fazla 2 ondalık basamak.";
    else if (type === "Percent" && parsed.value > 100) next.value = "Oran en fazla 100 olabilir.";
    if (target === "Category" && !categoryId) next.categoryId = "Kategori seçin.";
    if (target === "Item" && !product) next.productId = "Ürün veya hizmet seçin.";
    setErrors(next);
    setFormError(null);
    if (Object.keys(next).length) return;
    save.mutate({
      staffMemberId: staffId || null,
      target,
      categoryId: target === "Category" ? categoryId : null,
      productId: target === "Item" ? product!.id : null,
      type,
      value: parsed!.value,
      isActive: active,
    });
  };

  return (
    <Dialog open onOpenChange={(v) => !v && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{rule ? "Prim kuralını düzenle" : "Prim kuralı ekle"}</DialogTitle>
          <DialogDescription>Değişiklik yeni satışlara uygulanır; yazılmış primler değişmez.</DialogDescription>
        </DialogHeader>
        <form
          className="flex flex-col gap-4"
          noValidate
          onSubmit={(e) => {
            e.preventDefault();
            submit();
          }}
        >
          <Field label="Kime" error={errors.staffMemberId}>
            <select className={selectClass} value={staffId} onChange={(e) => setStaffId(e.target.value)}>
              <option value="">Herkes</option>
              {staff.data?.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.fullName}
                </option>
              ))}
            </select>
          </Field>
          <Field label="Neye" error={errors.target}>
            <select className={selectClass} value={target} onChange={(e) => setTarget(e.target.value as CommissionTarget)}>
              {targets.map((t) => (
                <option key={t} value={t}>
                  {commissionTargetNames[t]}
                </option>
              ))}
            </select>
          </Field>
          {target === "Category" && (
            <Field label="Kategori" error={errors.categoryId}>
              <select className={selectClass} value={categoryId} onChange={(e) => setCategoryId(e.target.value)}>
                <option value="">Seçin</option>
                {categories.data?.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </select>
            </Field>
          )}
          {target === "Item" && (
            <div className="flex flex-col gap-2">
              <span className="text-sm font-semibold text-black">Ürün veya hizmet</span>
              {product ? (
                <div className="flex min-h-11 items-center justify-between gap-2 border border-border px-3">
                  <span className="truncate text-sm text-black">{product.name}</span>
                  <Button type="button" variant="ghost" onClick={() => setProduct(null)}>
                    Değiştir
                  </Button>
                </div>
              ) : (
                <ProductSearch onPick={(p) => setProduct({ id: p.id, name: p.name })} placeholder="Ürün veya hizmet ara" />
              )}
              {errors.productId && <FormMessage>{errors.productId}</FormMessage>}
            </div>
          )}
          <div role="group" aria-labelledby="commission-value-label" className="flex flex-col gap-2">
            <span id="commission-value-label" className="text-sm font-semibold text-black">
              Prim
            </span>
            <div className="grid grid-cols-[minmax(0,1fr)_7rem] gap-2">
              <select
                className={selectClass}
                value={type}
                onChange={(e) => setType(e.target.value as CommissionType)}
                aria-label="Prim türü"
              >
                <option value="Percent">Yüzde (%)</option>
                <option value="Fixed">Adet başına TL</option>
              </select>
              <NumberInput
                fixed={type === "Fixed" ? 2 : undefined}
                value={value}
                onChange={(e) => setValue(e.target.value)}
                placeholder={type === "Percent" ? "ör. 10" : "ör. 50,00"}
                aria-label={type === "Percent" ? "Oran (yüzde)" : "Tutar (TL, adet başına)"}
                aria-invalid={errors.value ? true : undefined}
              />
            </div>
            {errors.value && <FormMessage>{errors.value}</FormMessage>}
          </div>
          {rule && (
            <label className="flex min-h-11 items-center gap-3 text-sm text-black">
              <Switch checked={active} onCheckedChange={setActive} />
              Aktif
            </label>
          )}
          {formError && <FormMessage>{formError}</FormMessage>}
          <DialogFooter className="flex-row justify-between gap-2 sm:justify-between">
            {rule ? (
              <Button type="button" variant="destructive" onClick={() => remove.mutate()} disabled={remove.isPending}>
                <Trash2 aria-hidden />
                Sil
              </Button>
            ) : (
              <span />
            )}
            <Button type="submit" disabled={save.isPending}>
              Kaydet
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

type RuleBody = {
  staffMemberId: string | null;
  target: CommissionTarget;
  categoryId: string | null;
  productId: string | null;
  type: CommissionType;
  value: number;
  isActive: boolean;
};

function saveRule(id: string | null, body: RuleBody) {
  return id
    ? unwrap(api.PUT("/api/commission-rules/{id}", { params: { path: { id } }, body }))
    : unwrap(api.POST("/api/commission-rules", { body }));
}
