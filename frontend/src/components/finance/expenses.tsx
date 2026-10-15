"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import { cn } from "cn";
import { Check, Plus, ReceiptText, Undo2 } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { toast } from "sonner";

import { EmptyState, selectClass } from "@/components/contacts/contact-bits";
import { QueryError } from "@/components/feedback/query-error";
import { Field, FormMessage } from "@/components/forms/field";
import { NegativeBalanceWarning, ReverseDialog } from "@/components/finance/cash-dialogs";
import { MoneyField, amountOf, useCashAccounts, useFinanceRefresh } from "@/components/finance/finance-dialogs";
import { RangePicker, type RangeValue, resolveRange } from "@/components/finance/range-picker";
import { useSession } from "@/components/providers/session-provider";
import { useStaffList } from "@/components/staff/staff-bits";
import { ContactPicker } from "@/components/sales/contact-picker";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { api, errorMessage, fieldErrors, unwrap } from "@/lib/api/client";
import type { ExpenseCategoryKind, ExpenseItem } from "@/lib/api/types";
import type { CartContact } from "@/lib/cart";
import { formatDayShort, formatMoney, todayIstanbul } from "@/lib/format";
import { can } from "@/lib/permissions";
import { validateMoney } from "@/lib/validation/rules";

export const expenseCategoriesKey = ["expense-categories"] as const;

export function useExpenseCategories(includeInactive = false, enabled = true) {
  return useQuery({
    queryKey: [...expenseCategoriesKey, includeInactive],
    queryFn: () => unwrap(api.GET("/api/expense-categories", { params: { query: { includeInactive } } })),
    enabled,
  });
}

const kindFilters: { key: "" | ExpenseCategoryKind; label: string }[] = [
  { key: "", label: "Hepsi" },
  { key: "Expense", label: "Giderler" },
  { key: "Income", label: "Gelirler" },
];

/** Cari ve Kasa → Gelir-gider: tarih ve tür süzgeci, net toplam (ters kayıtlar düşülmüş), ters kayıt. */
export function ExpenseList() {
  const { role } = useSession();
  const [range, setRange] = useState<RangeValue>({ preset: "bu-ay" });
  const [kind, setKind] = useState<"" | ExpenseCategoryKind>("Expense");
  const [categoryId, setCategoryId] = useState("");
  const [page, setPage] = useState(1);
  const [reversing, setReversing] = useState<ExpenseItem | null>(null);
  const { from, to } = resolveRange(range);
  const categories = useExpenseCategories(true);
  const query = { from, to, kind: kind || undefined, categoryId: categoryId || undefined, page, pageSize: 50 };
  const list = useQuery({ queryKey: ["expenses", query], queryFn: () => unwrap(api.GET("/api/expenses", { params: { query } })) });

  return (
    <div className="flex flex-col gap-5">
      {can(role, "manageFinance") && (
        <div className="grid grid-cols-2 gap-2 sm:flex">
          <Button asChild>
            <Link href="/panel/cari-kasa/gelir-gider/yeni?tur=gider">
              <ReceiptText aria-hidden />
              Gider ekle
            </Link>
          </Button>
          <Button variant="outline" asChild>
            <Link href="/panel/cari-kasa/gelir-gider/yeni?tur=gelir">
              <Plus aria-hidden />
              Gelir ekle
            </Link>
          </Button>
        </div>
      )}
      <RangePicker value={range} onChange={(v) => (setRange(v), setPage(1))} />
      <div className="grid gap-3 sm:grid-cols-2 lg:max-w-2xl">
        <label className="flex flex-col gap-1 text-sm font-semibold text-black">
          Tür
          <select className={selectClass} value={kind} onChange={(e) => (setKind(e.target.value as typeof kind), setCategoryId(""), setPage(1))}>
            {kindFilters.map((k) => (
              <option key={k.key} value={k.key}>
                {k.label}
              </option>
            ))}
          </select>
        </label>
        <label className="flex flex-col gap-1 text-sm font-semibold text-black">
          Kategori
          <select className={selectClass} value={categoryId} onChange={(e) => (setCategoryId(e.target.value), setPage(1))}>
            <option value="">Tüm kategoriler</option>
            {categories.data
              ?.filter((c) => !kind || c.kind === kind)
              .map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                  {!c.isActive ? " (pasif)" : ""}
                </option>
              ))}
          </select>
        </label>
      </div>
      {list.isPending ? (
        <p role="status" className="text-sm text-neutral-600">
          Yükleniyor…
        </p>
      ) : list.isError ? (
        <QueryError error={list.error} onRetry={list.refetch} />
      ) : list.data.items.length === 0 ? (
        <EmptyState icon={<ReceiptText className="size-10" />} title="Bu aralıkta kayıt yok" />
      ) : (
        <>
          <p className="flex items-baseline justify-between border-b border-black pb-2 text-base font-semibold text-black">
            <span>
              {kind === "Income" ? "Gelir toplamı" : kind === "Expense" ? "Gider toplamı" : "Toplam"} · {list.data.total} kayıt
            </span>
            <span className="tabular-nums">{formatMoney(list.data.sum)}</span>
          </p>
          <ul className="flex flex-col divide-y divide-border border-b border-border">
            {list.data.items.map((i) => (
              <li key={i.id} className={cn("flex items-start gap-3 py-3", (i.isReversal || i.isReversed) && "text-neutral-700")}>
                <span className="flex min-w-0 flex-1 flex-col gap-0.5">
                  <span className="text-sm font-semibold text-black">{i.description}</span>
                  <span className="text-xs text-neutral-600">
                    {formatDayShort(i.date)} · {i.kind === "Income" ? "Gelir" : "Gider"} · {i.categoryName} · {i.cashAccountName}
                    {i.contactName && ` · ${i.contactName}`}
                    {i.documentNo && ` · Belge ${i.documentNo}`}
                  </span>
                  {i.isReversed && <span className="text-xs text-neutral-700">Ters kaydı yapıldı</span>}
                </span>
                <span className="text-sm font-semibold text-black tabular-nums">
                  {i.netAmount < 0 ? "−" : ""}
                  {formatMoney(Math.abs(i.netAmount))}
                </span>
                {i.canReverse && (
                  <Button variant="ghost" size="icon" aria-label="Ters kayıt" title="Ters kayıt" onClick={() => setReversing(i)}>
                    <Undo2 aria-hidden />
                  </Button>
                )}
              </li>
            ))}
          </ul>
          {list.data.total > page * 50 && (
            <Button variant="outline" className="self-start" onClick={() => setPage((p) => p + 1)}>
              Sonraki sayfa
            </Button>
          )}
        </>
      )}
      {reversing && (
        <ReverseDialog
          item={{
            id: reversing.id,
            date: reversing.date,
            typeLabel: reversing.kind === "Income" ? "Gelir" : "Gider",
            amount: reversing.amount,
            description: reversing.description,
            counterAccountName: null,
          }}
          open
          onOpenChange={(v) => !v && setReversing(null)}
        />
      )}
    </div>
  );
}

/**
 * Gider/gelir ekle (telefonda hızlı işlem): tutar, büyük kategori düğmeleri, hesap, tarih; açıklama, belge no ve tedarikçi
 * isteğe bağlı. Tedarikçi sadece bilgidir, cariye işlenmez (borç ödemesi "Ödeme yap" ile). Maaş kategorisinde (sadece Sahip)
 * isteğe bağlı personel seçilir; personel kartındaki "Maaş öde" formu personel ve maaş kategorisi seçili açar (staffId).
 */
export function ExpenseForm({ kind, staffId: initialStaffId }: { kind: ExpenseCategoryKind; staffId?: string | null }) {
  const router = useRouter();
  const { role } = useSession();
  const refresh = useFinanceRefresh();
  const categories = useExpenseCategories();
  const accounts = useCashAccounts();
  const [amount, setAmount] = useState("");
  const [categoryId, setCategoryId] = useState("");
  const [accountId, setAccountId] = useState("");
  const [date, setDate] = useState(todayIstanbul());
  const [description, setDescription] = useState("");
  const [documentNo, setDocumentNo] = useState("");
  const [supplier, setSupplier] = useState<CartContact | null>(null);
  const [staffId, setStaffId] = useState(initialStaffId ?? "");
  const staff = useStaffList();
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [saved, setSaved] = useState<{ amount: number; balance: number; account: string } | null>(null);
  const isExpense = kind === "Expense";
  const options = (categories.data ?? []).filter((c) => c.kind === kind && (!c.isSalary || role === "Owner"));
  const salaryCategory = options.find((c) => c.isSalary);
  const selectedCategoryId = categoryId || (initialStaffId && salaryCategory ? salaryCategory.id : "");
  const isSalary = options.find((c) => c.id === selectedCategoryId)?.isSalary ?? false;
  const account =
    accounts.data?.find((a) => a.id === accountId) ?? accounts.data?.find((a) => a.isDefault && a.type === "Cash") ?? accounts.data?.[0];

  const save = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/expenses", {
          body: {
            kind,
            date,
            categoryId: selectedCategoryId,
            amount: amountOf(amount),
            cashAccountId: account!.id,
            description: description.trim() || null,
            documentNo: documentNo.trim() || null,
            contactId: supplier?.id ?? null,
            staffMemberId: isSalary && staffId ? staffId : null,
          },
        }),
      ),
    onSuccess: async (r) => {
      setSaved({ amount: amountOf(amount), balance: r.accountBalance, account: account!.name });
      toast.success(isExpense ? "Gider kaydedildi." : "Gelir kaydedildi.");
      await refresh();
    },
    onError: (e) => {
      const fields = fieldErrors(e);
      setErrors(Object.keys(fields).length ? fields : { form: errorMessage(e) });
    },
  });

  const reset = () => {
    setSaved(null);
    setAmount("");
    setDescription("");
    setDocumentNo("");
    setSupplier(null);
    setErrors({});
  };

  if (saved)
    return (
      <div className="flex flex-col gap-4">
        <p className="flex items-center gap-2 text-lg font-semibold text-black">
          <Check aria-hidden className="size-6 text-brand" />
          {formatMoney(saved.amount)} {isExpense ? "gider" : "gelir"} kaydedildi.
        </p>
        <p className="text-sm text-neutral-700">
          {saved.account} bakiyesi: {formatMoney(saved.balance)}
          {saved.balance < 0 && " (eksi bakiye)"}
        </p>
        <div className="grid grid-cols-2 gap-2 sm:flex">
          <Button onClick={reset}>Bir tane daha</Button>
          <Button variant="outline" asChild>
            <Link href="/panel/cari-kasa?sekme=gelir-gider">Listeye dön</Link>
          </Button>
        </div>
      </div>
    );

  const submit = () => {
    const next: Record<string, string> = {};
    const moneyError = validateMoney(amount);
    if (moneyError) next.amount = moneyError;
    if (!selectedCategoryId) next.categoryId = "Kategori seçin.";
    if (!account) next.cashAccountId = "Kasa veya banka hesabı seçin.";
    setErrors(next);
    if (Object.keys(next).length === 0) save.mutate();
  };

  return (
    <div className="flex flex-col gap-5">
      <MoneyField label="Tutar (TL)" value={amount} onChange={setAmount} error={errors.amount} autoFocus />
      <fieldset className="flex flex-col gap-2">
        <legend className="mb-2 text-sm font-semibold text-black">Kategori</legend>
        {categories.isPending ? (
          <p role="status" className="text-sm text-neutral-600">
            Yükleniyor…
          </p>
        ) : (
          <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
            {options.map((c) => (
              <label
                key={c.id}
                className={cn(
                  "flex min-h-12 cursor-pointer items-center border px-3 text-sm font-semibold",
                  selectedCategoryId === c.id ? "border-black bg-black text-white" : "border-border text-black hover:border-black",
                )}
              >
                <input
                  type="radio"
                  name="category"
                  value={c.id}
                  checked={selectedCategoryId === c.id}
                  onChange={() => setCategoryId(c.id)}
                  className="sr-only"
                />
                {c.name}
              </label>
            ))}
          </div>
        )}
        {errors.categoryId && (
          <p role="alert" className="text-sm text-danger-600">
            {errors.categoryId}
          </p>
        )}
      </fieldset>
      {isSalary && (
        <Field label="Personel (isteğe bağlı)" error={errors.staffMemberId} hint="Seçilirse personel kartında Ödenen maaşlar listesinde görünür.">
          <select className={selectClass} value={staffId} onChange={(e) => setStaffId(e.target.value)}>
            <option value="">Seçilmedi</option>
            {staff.data?.map((s) => (
              <option key={s.id} value={s.id}>
                {s.fullName}
              </option>
            ))}
          </select>
        </Field>
      )}
      <div className="grid grid-cols-2 gap-3">
        <Field label={isExpense ? "Nereden ödendi?" : "Nereye girdi?"} error={errors.cashAccountId}>
          <select className={selectClass} value={account?.id ?? ""} onChange={(e) => setAccountId(e.target.value)}>
            {accounts.data?.map((a) => (
              <option key={a.id} value={a.id}>
                {a.name}
              </option>
            ))}
          </select>
        </Field>
        <Field label="Tarih" error={errors.date}>
          <Input type="date" value={date} onChange={(e) => setDate(e.target.value)} />
        </Field>
      </div>
      {isExpense && <NegativeBalanceWarning account={account} amountText={amount} />}
      <Field label="Açıklama (isteğe bağlı)" error={errors.description}>
        <Input value={description} onChange={(e) => setDescription(e.target.value)} maxLength={500} placeholder={isExpense ? "ör. Ekim kirası" : ""} />
      </Field>
      <Field label="Belge no (isteğe bağlı)" error={errors.documentNo}>
        <Input value={documentNo} onChange={(e) => setDocumentNo(e.target.value)} maxLength={50} placeholder="Fatura veya fiş no" />
      </Field>
      {isExpense && <ContactPicker kind="supplier" value={supplier} onChange={setSupplier} />}
      {errors.form && <FormMessage>{errors.form}</FormMessage>}
      <div className="grid grid-cols-2 gap-2 sm:flex">
        <Button size="lg" onClick={submit} disabled={save.isPending}>
          {save.isPending ? "Kaydediliyor…" : "Kaydet"}
        </Button>
        <Button size="lg" variant="outline" onClick={() => router.back()}>
          Vazgeç
        </Button>
      </div>
    </div>
  );
}
