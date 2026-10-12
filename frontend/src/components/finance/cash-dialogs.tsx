"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { toast } from "sonner";

import { Notice, selectClass } from "@/components/contacts/contact-bits";
import { Field, FormMessage } from "@/components/forms/field";
import { DialogShell, MoneyField, amountOf, useCashAccounts, useFinanceRefresh } from "@/components/finance/finance-dialogs";
import { ContactPicker } from "@/components/sales/contact-picker";
import { Button } from "@/components/ui/button";
import { DialogFooter } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { api, errorMessage, fieldErrors, unwrap } from "@/lib/api/client";
import type { CashAccount, CashAccountType, CashTransactionItem } from "@/lib/api/types";
import type { CartContact } from "@/lib/cart";
import { formatDayShort, formatMoney, todayIstanbul } from "@/lib/format";
import { IBAN_MESSAGE, formatIban, moneyToInput, normalizeIban, parseMoneyInput, validateMoney } from "@/lib/validation/rules";

/** Satış ekranı gibi her rolün gördüğü hesap seçenekleri (bakiyesiz). */
export function useCashAccountOptions(enabled = true) {
  return useQuery({
    queryKey: ["cash-accounts", "options"],
    queryFn: () => unwrap(api.GET("/api/cash-accounts/options")),
    enabled,
    staleTime: 60_000,
  });
}

/** Çıkıştan sonra bakiye eksiye düşecekse uyarı (engellemez; karar günlüğü 2026-10-12). */
export function NegativeBalanceWarning({ account, amountText }: { account: CashAccount | undefined; amountText: string }) {
  const amount = parseMoneyInput(amountText)?.value;
  if (!account || !amount || account.balance - amount >= 0) return null;
  return (
    <Notice>
      Bu işlemden sonra {account.name} bakiyesi {formatMoney(account.balance - amount)} olacak (eksi bakiye).
    </Notice>
  );
}

/** Hesap ekle veya düzenle. Açılış bakiyesi sadece eklerken (eksi olabilir: KMH); sonradan ters kayıtla düzeltilir. */
export function AccountDialog({
  account,
  open,
  onOpenChange,
  onSaved,
}: {
  account?: CashAccount;
  open: boolean;
  onOpenChange: (v: boolean) => void;
  onSaved?: (a: CashAccount) => void;
}) {
  const refresh = useFinanceRefresh();
  const [type, setType] = useState<CashAccountType>(account?.type ?? "Bank");
  const [name, setName] = useState(account?.name ?? "");
  const [iban, setIban] = useState(account?.iban ? formatIban(account.iban) : "");
  const [holder, setHolder] = useState(account?.ibanHolderName ?? "");
  const [opening, setOpening] = useState("");
  const [negative, setNegative] = useState(false);
  const [openingDate, setOpeningDate] = useState(todayIstanbul());
  const [errors, setErrors] = useState<Record<string, string>>({});

  const save = useMutation({
    mutationFn: () => {
      const bank = type === "Bank";
      const body = { name: name.trim(), iban: bank && iban.trim() ? normalizeIban(iban) : null, ibanHolderName: bank ? holder.trim() || null : null };
      if (account) return unwrap(api.PUT("/api/cash-accounts/{id}", { params: { path: { id: account.id } }, body }));
      const value = opening.trim() ? amountOf(opening) : 0;
      return unwrap(
        api.POST("/api/cash-accounts", {
          body: { ...body, type, openingBalance: value ? (negative ? -value : value) : null, openingDate },
        }),
      );
    },
    onSuccess: async (saved) => {
      toast.success(account ? "Hesap güncellendi." : "Hesap eklendi.");
      await refresh();
      onSaved?.(saved);
      onOpenChange(false);
    },
    onError: (e) => {
      const fields = fieldErrors(e);
      setErrors(Object.keys(fields).length ? fields : { form: errorMessage(e) });
    },
  });

  const submit = () => {
    const next: Record<string, string> = {};
    if (!name.trim()) next.name = "Hesap adı girin.";
    if (type === "Bank" && iban.trim() && !normalizeIban(iban)) next.iban = IBAN_MESSAGE;
    if (!account && opening.trim()) {
      const error = validateMoney(opening);
      if (error) next.openingBalance = error;
    }
    setErrors(next);
    if (Object.keys(next).length === 0) save.mutate();
  };

  return (
    <DialogShell open={open} onOpenChange={onOpenChange} title={account ? "Hesabı düzenle" : "Hesap ekle"}>
      <div className="flex flex-col gap-4">
        {!account && (
          <fieldset className="grid grid-cols-2 gap-2">
            <legend className="mb-2 text-sm font-semibold text-black">Hesap türü</legend>
            {(
              [
                ["Cash", "Nakit kasa", "Çekmece, kasa"],
                ["Bank", "Banka", "Banka hesabı, POS"],
              ] as const
            ).map(([value, label, hint]) => (
              <label
                key={value}
                className={`flex min-h-14 cursor-pointer flex-col justify-center rounded-lg border px-3 py-2 ${type === value ? "border-black ring-1 ring-black" : "border-border"}`}
              >
                <input type="radio" name="account-type" value={value} checked={type === value} onChange={() => setType(value)} className="sr-only" />
                <span className="text-sm font-semibold text-black">{label}</span>
                <span className="text-xs text-neutral-600">{hint}</span>
              </label>
            ))}
          </fieldset>
        )}
        <Field label="Hesap adı" error={errors.name}>
          <Input value={name} onChange={(e) => setName(e.target.value)} maxLength={100} placeholder={type === "Bank" ? "ör. Ziraat Bankası" : "ör. Dükkan kasası"} />
        </Field>
        {type === "Bank" && (
          <>
            <Field label="IBAN (isteğe bağlı)" error={errors.iban}>
              <Input value={iban} onChange={(e) => setIban(e.target.value)} placeholder="TR00 0000 0000 0000 0000 0000 00" autoComplete="off" />
            </Field>
            <Field label="Hesap sahibi (isteğe bağlı)" error={errors.ibanHolderName}>
              <Input value={holder} onChange={(e) => setHolder(e.target.value)} maxLength={100} />
            </Field>
          </>
        )}
        {!account && (
          <>
            <MoneyField label="Açılış bakiyesi (TL, isteğe bağlı)" value={opening} onChange={setOpening} error={errors.openingBalance} />
            <div className="grid grid-cols-2 gap-3">
              <label className="flex min-h-11 items-center gap-2 text-sm text-black">
                <input type="checkbox" checked={negative} onChange={(e) => setNegative(e.target.checked)} className="size-5" />
                Eksi bakiye (KMH)
              </label>
              <Field label="Bakiye tarihi">
                <Input type="date" value={openingDate} onChange={(e) => setOpeningDate(e.target.value)} />
              </Field>
            </div>
          </>
        )}
        {errors.form && <FormMessage>{errors.form}</FormMessage>}
      </div>
      <DialogFooter>
        <Button variant="ghost" onClick={() => onOpenChange(false)}>
          Vazgeç
        </Button>
        <Button onClick={submit} disabled={save.isPending}>
          {save.isPending ? "Kaydediliyor…" : "Kaydet"}
        </Button>
      </DialogFooter>
    </DialogShell>
  );
}

/** Hesaplar arası transfer: tek işlemde çıkış ve giriş. */
export function TransferDialog({ open, onOpenChange, fromId }: { open: boolean; onOpenChange: (v: boolean) => void; fromId?: string }) {
  const refresh = useFinanceRefresh();
  const accounts = useCashAccounts(open);
  const list = accounts.data ?? [];
  const [from, setFrom] = useState(fromId ?? "");
  const [to, setTo] = useState("");
  const [amount, setAmount] = useState("");
  const [date, setDate] = useState(todayIstanbul());
  const [description, setDescription] = useState("");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const fromAccount = list.find((a) => a.id === (from || list[0]?.id));
  const toAccount = list.find((a) => a.id === (to || list.find((x) => x.id !== fromAccount?.id)?.id));

  const save = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/cash-transfers", {
          body: { fromAccountId: fromAccount!.id, toAccountId: toAccount!.id, amount: amountOf(amount), date, description: description.trim() || null },
        }),
      ),
    onSuccess: async () => {
      toast.success("Transfer kaydedildi.");
      await refresh();
      onOpenChange(false);
    },
    onError: (e) => {
      const fields = fieldErrors(e);
      setErrors(Object.keys(fields).length ? fields : { form: errorMessage(e) });
    },
  });

  const submit = () => {
    const next: Record<string, string> = {};
    const moneyError = validateMoney(amount);
    if (moneyError) next.amount = moneyError;
    if (!fromAccount || !toAccount) next.form = "En az iki aktif hesap gerekli.";
    else if (fromAccount.id === toAccount.id) next.toAccountId = "Aynı hesaba transfer yapılamaz.";
    setErrors(next);
    if (Object.keys(next).length === 0) save.mutate();
  };

  return (
    <DialogShell open={open} onOpenChange={onOpenChange} title="Hesaplar arası transfer" description="Örneğin kasadaki nakdi bankaya yatırdınız.">
      <div className="flex flex-col gap-4">
        <div className="grid gap-3 sm:grid-cols-2">
          <Field label="Nereden" error={errors.fromAccountId}>
            <select className={selectClass} value={fromAccount?.id ?? ""} onChange={(e) => setFrom(e.target.value)}>
              {list.map((a) => (
                <option key={a.id} value={a.id}>
                  {a.name} ({formatMoney(a.balance)})
                </option>
              ))}
            </select>
          </Field>
          <Field label="Nereye" error={errors.toAccountId}>
            <select className={selectClass} value={toAccount?.id ?? ""} onChange={(e) => setTo(e.target.value)}>
              {list.map((a) => (
                <option key={a.id} value={a.id}>
                  {a.name}
                </option>
              ))}
            </select>
          </Field>
        </div>
        <MoneyField label="Tutar (TL)" value={amount} onChange={setAmount} error={errors.amount} autoFocus />
        <NegativeBalanceWarning account={fromAccount} amountText={amount} />
        <div className="grid grid-cols-2 gap-3">
          <Field label="Tarih">
            <Input type="date" value={date} onChange={(e) => setDate(e.target.value)} />
          </Field>
          <Field label="Açıklama (isteğe bağlı)">
            <Input value={description} onChange={(e) => setDescription(e.target.value)} maxLength={500} />
          </Field>
        </div>
        {errors.form && <FormMessage>{errors.form}</FormMessage>}
      </div>
      <DialogFooter>
        <Button variant="ghost" onClick={() => onOpenChange(false)}>
          Vazgeç
        </Button>
        <Button onClick={submit} disabled={save.isPending}>
          {save.isPending ? "Kaydediliyor…" : "Transferi kaydet"}
        </Button>
      </DialogFooter>
    </DialogShell>
  );
}

/** Cariye (tedarikçiye) ödeme: kasadan çıkış + cariye borç. Tedarikçi verilmezse pencerede seçilir. */
export function PaymentDialog({
  contact: given,
  open,
  onOpenChange,
}: {
  contact?: { id: string; displayName: string; balance?: number | null };
  open: boolean;
  onOpenChange: (v: boolean) => void;
}) {
  const refresh = useFinanceRefresh();
  const accounts = useCashAccounts(open);
  const [picked, setPicked] = useState<CartContact | null>(null);
  const contactId = given?.id ?? picked?.id;
  const debt = given?.balance && given.balance < 0 ? -given.balance : 0;
  const [amount, setAmount] = useState(debt ? moneyToInput(debt) : "");
  const [accountId, setAccountId] = useState("");
  const [date, setDate] = useState(todayIstanbul());
  const [description, setDescription] = useState("");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const account = accounts.data?.find((a) => a.id === accountId) ?? accounts.data?.find((a) => a.isDefault && a.type === "Bank") ?? accounts.data?.[0];

  const save = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/contacts/{id}/payments", {
          params: { path: { id: contactId! } },
          body: { cashAccountId: account!.id, amount: amountOf(amount), date, description: description.trim() || null },
        }),
      ),
    onSuccess: async (r) => {
      const remaining = r.contactBalance < 0 ? `Kalan borcunuz: ${formatMoney(-r.contactBalance)}` : "Borç kalmadı.";
      toast.success(`Ödeme kaydedildi. ${remaining}`);
      await refresh();
      onOpenChange(false);
    },
    onError: (e) => {
      const fields = fieldErrors(e);
      setErrors(Object.keys(fields).length ? fields : { form: errorMessage(e) });
    },
  });

  const submit = () => {
    const next: Record<string, string> = {};
    const moneyError = validateMoney(amount);
    if (moneyError) next.amount = moneyError;
    if (!contactId) next.contact = "Tedarikçi seçin.";
    if (!account) next.cashAccountId = "Kasa veya banka hesabı seçin.";
    setErrors(next);
    if (Object.keys(next).length === 0) save.mutate();
  };

  return (
    <DialogShell open={open} onOpenChange={onOpenChange} title="Ödeme yap" description={given?.displayName ?? "Tedarikçiye borç ödemesi"}>
      <div className="flex flex-col gap-4">
        {!given && <ContactPicker kind="supplier" value={picked} onChange={setPicked} required error={errors.contact} />}
        {given && debt > 0 && <p className="text-sm text-neutral-700">Borcunuz: {formatMoney(debt)}</p>}
        <MoneyField label="Ödenen tutar (TL)" value={amount} onChange={setAmount} error={errors.amount} />
        <div className="grid grid-cols-2 gap-3">
          <Field label="Nereden ödendi?" error={errors.cashAccountId}>
            <select className={selectClass} value={account?.id ?? ""} onChange={(e) => setAccountId(e.target.value)}>
              {accounts.data?.map((a) => (
                <option key={a.id} value={a.id}>
                  {a.name}
                </option>
              ))}
            </select>
          </Field>
          <Field label="Tarih">
            <Input type="date" value={date} onChange={(e) => setDate(e.target.value)} />
          </Field>
        </div>
        <NegativeBalanceWarning account={account} amountText={amount} />
        <Field label="Açıklama (isteğe bağlı)">
          <Input value={description} onChange={(e) => setDescription(e.target.value)} maxLength={500} />
        </Field>
        {errors.form && <FormMessage>{errors.form}</FormMessage>}
      </div>
      <DialogFooter>
        <Button variant="ghost" onClick={() => onOpenChange(false)}>
          Vazgeç
        </Button>
        <Button onClick={submit} disabled={save.isPending}>
          {save.isPending ? "Kaydediliyor…" : "Ödemeyi kaydet"}
        </Button>
      </DialogFooter>
    </DialogShell>
  );
}

/** Ters kayıt onayı: bugünün tarihiyle karşı kayıt; orijinal kayıt silinmez. */
export function ReverseDialog({
  item,
  open,
  onOpenChange,
}: {
  item: Pick<CashTransactionItem, "id" | "date" | "typeLabel" | "amount" | "description" | "counterAccountName">;
  open: boolean;
  onOpenChange: (v: boolean) => void;
}) {
  const refresh = useFinanceRefresh();
  const [reason, setReason] = useState("");
  const save = useMutation({
    mutationFn: () => unwrap(api.POST("/api/cash-transactions/{id}/reverse", { params: { path: { id: item.id } }, body: { reason: reason.trim() || null } })),
    onSuccess: async () => {
      toast.success("Ters kayıt yapıldı.");
      await refresh();
      onOpenChange(false);
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  return (
    <DialogShell
      open={open}
      onOpenChange={onOpenChange}
      title="Ters kayıt"
      description={`${formatDayShort(item.date)} tarihli ${item.typeLabel.toLocaleLowerCase("tr-TR")} · ${formatMoney(item.amount)}`}
    >
      <div className="flex flex-col gap-4">
        <p className="text-sm text-neutral-700">
          Kayıt silinmez. Bugünün tarihiyle karşı kayıt yazılır ve bakiye eski haline döner.
          {item.counterAccountName && ` Transferin ${item.counterAccountName} tarafı da geri alınır.`}
        </p>
        <p className="text-sm font-semibold text-black">{item.description}</p>
        <Field label="Neden (isteğe bağlı)">
          <Input value={reason} onChange={(e) => setReason(e.target.value)} maxLength={200} placeholder="ör. Yanlış tutar" />
        </Field>
      </div>
      <DialogFooter>
        <Button variant="ghost" onClick={() => onOpenChange(false)}>
          Vazgeç
        </Button>
        <Button onClick={() => save.mutate()} disabled={save.isPending}>
          {save.isPending ? "Kaydediliyor…" : "Ters kaydı yap"}
        </Button>
      </DialogFooter>
    </DialogShell>
  );
}
