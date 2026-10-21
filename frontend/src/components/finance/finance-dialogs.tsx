"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Check, Copy, Mail, MessageCircle, MessageSquareText } from "lucide-react";
import Link from "next/link";
import { useEffect, useRef, useState, type ReactNode } from "react";
import { toast } from "sonner";

import { contactsKey } from "@/components/contacts/contact-list";
import { Balance, Notice, selectClass } from "@/components/contacts/contact-bits";
import { ContactPicker } from "@/components/sales/contact-picker";
import { Field, FormMessage } from "@/components/forms/field";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { NumberInput } from "@/components/ui/number-input";
import { Textarea } from "@/components/ui/textarea";
import { API_URL, ApiError, api, errorMessage, fieldErrors, unwrap } from "@/lib/api/client";
import type { Contact, PaymentRequest } from "@/lib/api/types";
import type { CartContact } from "@/lib/cart";
import { formatDate, formatMoney, todayIstanbul } from "@/lib/format";
import { moneyToInput, parseMoneyInput, validateMoney } from "@/lib/validation/rules";

export const cashAccountsKey = ["cash-accounts"] as const;
export const paymentRequestsKey = ["payment-requests"] as const;

/** Cari/kasa değişince ilgili tüm sorgular tazelenir (liste bakiyeleri, kart, hareketler, kasa, linkler). */
export function useFinanceRefresh() {
  const queryClient = useQueryClient();
  return () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: contactsKey }),
      queryClient.invalidateQueries({ queryKey: cashAccountsKey }),
      queryClient.invalidateQueries({ queryKey: paymentRequestsKey }),
      queryClient.invalidateQueries({ queryKey: ["ledger"] }),
      queryClient.invalidateQueries({ queryKey: ["timeline"] }),
      queryClient.invalidateQueries({ queryKey: ["cash-transactions"] }),
      queryClient.invalidateQueries({ queryKey: ["expenses"] }),
      queryClient.invalidateQueries({ queryKey: ["receivables"] }),
      queryClient.invalidateQueries({ queryKey: ["dashboard"] }),
      queryClient.invalidateQueries({ queryKey: ["reports"] }),
    ]);
}

export function useCashAccounts(enabled = true) {
  return useQuery({ queryKey: cashAccountsKey, queryFn: () => unwrap(api.GET("/api/cash-accounts")), enabled });
}

export function DialogShell({
  open,
  onOpenChange,
  title,
  description,
  children,
}: {
  open: boolean;
  onOpenChange: (v: boolean) => void;
  title: string;
  description?: string;
  children: ReactNode;
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          {description && <DialogDescription>{description}</DialogDescription>}
        </DialogHeader>
        {children}
      </DialogContent>
    </Dialog>
  );
}

/** Para alanı: Türkçe biçimde yazılır ("1.250,50"); hata mesajları backend'le aynı. */
export function MoneyField({
  label,
  value,
  onChange,
  error,
  autoFocus,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  error?: string | null;
  autoFocus?: boolean;
}) {
  return (
    <Field label={label} error={error ?? undefined}>
      <NumberInput placeholder="0,00" value={value} autoFocus={autoFocus} onChange={(e) => onChange(e.target.value)} />
    </Field>
  );
}

export function amountOf(text: string) {
  return parseMoneyInput(text)!.value;
}

/** Elle borç/alacak kaydı. */
export function LedgerEntryDialog({
  contact,
  open,
  onOpenChange,
}: {
  contact: Contact;
  open: boolean;
  onOpenChange: (v: boolean) => void;
}) {
  const refresh = useFinanceRefresh();
  const [kind, setKind] = useState<"Debit" | "Credit">("Debit");
  const [amount, setAmount] = useState("");
  const [date, setDate] = useState(todayIstanbul());
  const [dueDate, setDueDate] = useState("");
  const [description, setDescription] = useState("");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [submitted, setSubmitted] = useState(false);
  const amountError = submitted ? validateMoney(amount) : null;

  const save = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/contacts/{id}/ledger", {
          params: { path: { id: contact.id } },
          body: { kind, amount: amountOf(amount), date, dueDate: dueDate || null, description: description.trim() },
        }),
      ),
    onSuccess: async () => {
      toast.success("Hareket kaydedildi.");
      await refresh();
      onOpenChange(false);
      setAmount("");
      setDescription("");
      setDueDate("");
      setSubmitted(false);
    },
    onError: (e) => setErrors({ ...fieldErrors(e), form: Object.keys(fieldErrors(e)).length ? "" : errorMessage(e) }),
  });

  const submit = () => {
    setSubmitted(true);
    const local: Record<string, string> = {};
    if (!description.trim()) local.description = "Açıklama girin.";
    if (dueDate && dueDate < date) local.dueDate = "Vade tarihi hareket tarihinden önce olamaz.";
    setErrors(local);
    if (validateMoney(amount) || Object.keys(local).length) return;
    save.mutate();
  };

  return (
    <DialogShell open={open} onOpenChange={onOpenChange} title="Borç / alacak ekle" description={contact.displayName}>
      <div className="flex flex-col gap-4">
        <fieldset className="grid grid-cols-2 gap-2">
          <legend className="sr-only">Hareket türü</legend>
          {(
            [
              ["Debit", "Borç", "Müşteri bize borçlandı"],
              ["Credit", "Alacak", "Borcu azaldı / biz borçlandık"],
            ] as const
          ).map(([value, label, hint]) => (
            <label
              key={value}
              className={`flex min-h-14 cursor-pointer flex-col justify-center rounded-lg border px-3 py-2 ${kind === value ? "border-black ring-1 ring-black" : "border-border"}`}
            >
              <input
                type="radio"
                name="kind"
                value={value}
                checked={kind === value}
                onChange={() => setKind(value)}
                className="sr-only"
              />
              <span className="text-sm font-semibold text-black">{label}</span>
              <span className="text-xs text-neutral-600">{hint}</span>
            </label>
          ))}
        </fieldset>
        <MoneyField label="Tutar (TL)" value={amount} onChange={setAmount} error={amountError ?? errors.amount} />
        <div className="grid grid-cols-2 gap-3">
          <Field label="Tarih" error={errors.date}>
            <Input type="date" value={date} onChange={(e) => setDate(e.target.value)} />
          </Field>
          <Field label="Vade (isteğe bağlı)" error={errors.dueDate}>
            <Input type="date" value={dueDate} min={date} onChange={(e) => setDueDate(e.target.value)} />
          </Field>
        </div>
        <Field label="Açıklama" error={errors.description}>
          <Input
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder="ör. Saç boyası, Ekim ayı aidatı"
            maxLength={500}
          />
        </Field>
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

/**
 * Elden/havaleyle gelen tahsilat: seçilen kasaya giriş + cariye alacak. Müşteri verilmezse (Bugün ekranı, kasa ekranı)
 * pencerede müşteri seçilir; tutar müşterinin alacak bakiyesiyle dolar.
 */
export function CollectionDialog({
  contact: given,
  open,
  onOpenChange,
}: {
  contact?: Pick<Contact, "id" | "displayName" | "balance">;
  open: boolean;
  onOpenChange: (v: boolean) => void;
}) {
  const refresh = useFinanceRefresh();
  const accounts = useCashAccounts(open);
  const [picked, setPicked] = useState<CartContact | null>(null);
  const pickedContact = useQuery({
    queryKey: [...contactsKey, picked?.id],
    queryFn: () => unwrap(api.GET("/api/contacts/{id}", { params: { path: { id: picked!.id } } })),
    enabled: !given && !!picked,
  });
  const contact = given ?? (picked && pickedContact.data ? pickedContact.data : null);
  // Elle yazılmadıysa (null) tutar müşterinin alacak bakiyesidir; müşteri değişince sıfırlanır.
  const [typed, setTyped] = useState<string | null>(null);
  const balance = contact?.balance ?? 0;
  const amount = typed ?? (balance > 0 ? moneyToInput(balance) : "");
  const setAmount = setTyped;
  const [date, setDate] = useState(todayIstanbul());
  const [accountId, setAccountId] = useState("");
  const [description, setDescription] = useState("");
  const [submitted, setSubmitted] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const selected = accountId || accounts.data?.find((a) => a.isDefault)?.id || "";
  const amountError = submitted ? validateMoney(amount) : null;

  const save = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/contacts/{id}/collections", {
          params: { path: { id: contact!.id } },
          body: { amount: amountOf(amount), date, cashAccountId: selected, description: description.trim() || null },
        }),
      ),
    onSuccess: async (r) => {
      toast.success(`Tahsilat kaydedildi. Kalan: ${r.contactBalance > 0 ? formatMoney(r.contactBalance) : "borç yok"}`);
      await refresh();
      onOpenChange(false);
      setSubmitted(false);
      setPicked(null);
    },
    onError: (e) => setError(errorMessage(e)),
  });

  return (
    <DialogShell open={open} onOpenChange={onOpenChange} title="Tahsilat al" description={given?.displayName}>
      <div className="flex flex-col gap-4">
        {!given && (
          <ContactPicker
            value={picked}
            onChange={(c) => {
              setPicked(c);
              setTyped(null);
            }}
            required
            error={submitted && !picked ? "Müşteri seçin." : null}
          />
        )}
        {!given && contact && (
          <p className="text-sm text-neutral-700">
            Güncel bakiye: <Balance value={contact.balance ?? 0} className="text-sm" />
          </p>
        )}
        <MoneyField label="Alınan tutar (TL)" value={amount} onChange={setAmount} error={amountError} />
        <div className="grid grid-cols-2 gap-3">
          <Field label="Nereye girdi?">
            <select className={selectClass} value={selected} onChange={(e) => setAccountId(e.target.value)}>
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
        <Field label="Açıklama (isteğe bağlı)">
          <Input value={description} onChange={(e) => setDescription(e.target.value)} maxLength={500} />
        </Field>
        {error && <FormMessage>{error}</FormMessage>}
      </div>
      <DialogFooter>
        <Button variant="ghost" onClick={() => onOpenChange(false)}>
          Vazgeç
        </Button>
        <Button
          onClick={() => {
            setSubmitted(true);
            if (!validateMoney(amount) && selected && contact) save.mutate();
          }}
          disabled={save.isPending || !selected}
        >
          {save.isPending ? "Kaydediliyor…" : "Tahsilatı kaydet"}
        </Button>
      </DialogFooter>
    </DialogShell>
  );
}

export function CopyButton({ text, label = "Kopyala" }: { text: string; label?: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <Button
      type="button"
      variant="outline"
      onClick={async () => {
        await navigator.clipboard.writeText(text);
        setCopied(true);
        setTimeout(() => setCopied(false), 2000);
      }}
    >
      {copied ? <Check aria-hidden /> : <Copy aria-hidden />}
      {copied ? "Kopyalandı" : label}
    </Button>
  );
}

/** Paylaşım linki hazır olunca: WhatsApp, kopyala, e-posta, SMS (Yakında). */
export function ShareButtons({
  url,
  whatsappUrl,
  onEmail,
  emailDisabled,
  emailBusy,
}: {
  url: string;
  whatsappUrl: string;
  onEmail?: () => void;
  emailDisabled?: string | null;
  emailBusy?: boolean;
}) {
  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center gap-2 rounded-lg border border-border bg-neutral-50 px-3 py-2">
        <span className="min-w-0 flex-1 truncate text-sm text-black">{url}</span>
      </div>
      <div className="grid grid-cols-2 gap-2">
        <Button asChild>
          <a href={whatsappUrl} target="_blank" rel="noopener noreferrer">
            <MessageCircle aria-hidden />
            WhatsApp
          </a>
        </Button>
        <CopyButton text={url} label="Linki kopyala" />
        {onEmail && (
          <Button
            variant="outline"
            onClick={onEmail}
            disabled={!!emailDisabled || emailBusy}
            title={emailDisabled ?? undefined}
          >
            <Mail aria-hidden />
            {emailBusy ? "Gönderiliyor…" : "E-postayla gönder"}
          </Button>
        )}
        <Button variant="outline" disabled title="SMS ile gönderim yakında">
          <MessageSquareText aria-hidden />
          SMS (Yakında)
        </Button>
      </div>
      {emailDisabled && <p className="text-xs text-neutral-600">{emailDisabled}</p>}
    </div>
  );
}

/** Ekstre: tarih aralığı → PDF indir veya 30 gün geçerli link (WhatsApp, e-posta, kopyala). */
export function StatementDialog({
  contact,
  open,
  onOpenChange,
  canShare,
}: {
  contact: Contact;
  open: boolean;
  onOpenChange: (v: boolean) => void;
  canShare: boolean;
}) {
  const refresh = useFinanceRefresh();
  const [from, setFrom] = useState("");
  const [to, setTo] = useState(todayIstanbul());
  const [link, setLink] = useState<{ url: string; whatsappUrl: string } | null>(null);
  const rangeError = from && to && from > to ? "Bitiş tarihi başlangıçtan önce olamaz." : null;
  const query = `${from ? `from=${from}&` : ""}to=${to}`;

  const share = useMutation({
    mutationFn: (channel: "Link" | "Email") =>
      unwrap(
        api.POST("/api/contacts/{id}/statement-shares", {
          params: { path: { id: contact.id } },
          body: { from: from || null, to, channel },
        }),
      ),
    onSuccess: async (r, channel) => {
      if (channel === "Email")
        toast.success(
          r.emailSent ? `${contact.email} adresine gönderildi.` : "E-posta gönderilemedi; linki kopyalayıp gönderin.",
        );
      setLink({ url: r.url, whatsappUrl: r.whatsappUrl });
      await refresh();
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  return (
    <DialogShell
      open={open}
      onOpenChange={(v) => {
        onOpenChange(v);
        if (!v) setLink(null);
      }}
      title="Cari ekstre"
      description={`${contact.displayName} · logolu PDF veya 30 gün geçerli link`}
    >
      <div className="flex flex-col gap-4">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Başlangıç" hint="Boşsa tüm hareketler">
            <Input
              type="date"
              value={from}
              max={to}
              onChange={(e) => {
                setFrom(e.target.value);
                setLink(null);
              }}
            />
          </Field>
          <Field label="Bitiş" error={rangeError ?? undefined}>
            <Input
              type="date"
              value={to}
              onChange={(e) => {
                setTo(e.target.value);
                setLink(null);
              }}
            />
          </Field>
        </div>
        <Button asChild variant="outline" disabled={!!rangeError}>
          <a href={`${API_URL}/api/contacts/${contact.id}/statement.pdf?${query}`} target="_blank" rel="noopener">
            PDF&apos;i aç / indir
          </a>
        </Button>
        {canShare &&
          (link ? (
            <ShareButtons
              url={link.url}
              whatsappUrl={link.whatsappUrl}
              onEmail={() => share.mutate("Email")}
              emailBusy={share.isPending}
              emailDisabled={contact.email ? null : "Müşterinin e-postası yok; kartına ekleyin."}
            />
          ) : (
            <Button onClick={() => share.mutate("Link")} disabled={!!rangeError || share.isPending}>
              {share.isPending ? "Hazırlanıyor…" : "Paylaşım linki oluştur"}
            </Button>
          ))}
      </div>
    </DialogShell>
  );
}

/** Tahsilat linki oluşturma: IBAN yoksa Ayarlar'a yönlendirir. */
export function PaymentLinkDialog({
  contact,
  open,
  onOpenChange,
}: {
  contact: Contact;
  open: boolean;
  onOpenChange: (v: boolean) => void;
}) {
  const refresh = useFinanceRefresh();
  const [amount, setAmount] = useState(contact.balance && contact.balance > 0 ? moneyToInput(contact.balance) : "");
  const [note, setNote] = useState("");
  const [sendEmail, setSendEmail] = useState(false);
  const [submitted, setSubmitted] = useState(false);
  const [ibanMissing, setIbanMissing] = useState(false);
  const [link, setLink] = useState<{ url: string; whatsappUrl: string } | null>(null);
  const amountError = submitted ? validateMoney(amount) : null;

  const create = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/payment-requests", {
          body: { contactId: contact.id, amount: amountOf(amount), note: note.trim() || null, sendEmail },
        }),
      ),
    onSuccess: async (r) => {
      setLink({ url: r.url, whatsappUrl: r.whatsappUrl });
      if (sendEmail)
        toast.success(
          r.emailSent ? `${contact.email} adresine gönderildi.` : "E-posta gönderilemedi; linki kopyalayıp gönderin.",
        );
      await refresh();
    },
    onError: (e) => {
      if (e instanceof ApiError && (e.problem as { code?: string }).code === "iban_required") setIbanMissing(true);
      else toast.error(errorMessage(e));
    },
  });

  return (
    <DialogShell
      open={open}
      onOpenChange={(v) => {
        onOpenChange(v);
        if (!v) {
          setLink(null);
          setSubmitted(false);
        }
      }}
      title="Tahsilat linki"
      description='Müşteri linkte borcu ve IBAN&apos;ınızı görür, ödeyince "Ödedim"e basar; size bildirim düşer. 30 gün geçerli.'
    >
      {ibanMissing ? (
        <div className="flex flex-col gap-3">
          <Notice>Tahsilat linki için önce işletmenin IBAN&apos;ını girin.</Notice>
          <Button asChild>
            <Link href="/panel/ayarlar?odeme=1">Ayarlar&apos;a git</Link>
          </Button>
        </div>
      ) : link ? (
        <ShareButtons url={link.url} whatsappUrl={link.whatsappUrl} />
      ) : (
        <>
          <div className="flex flex-col gap-4">
            <MoneyField label="İstenen tutar (TL)" value={amount} onChange={setAmount} error={amountError} />
            <Field label="Not (isteğe bağlı)" hint="Müşteri linkte görür.">
              <Textarea
                rows={2}
                value={note}
                onChange={(e) => setNote(e.target.value)}
                maxLength={500}
                placeholder="ör. Ekim ayı aidatı"
              />
            </Field>
            <label className="flex min-h-11 items-center gap-3 text-sm text-black">
              <input
                type="checkbox"
                className="size-5 accent-black"
                checked={sendEmail}
                disabled={!contact.email}
                onChange={(e) => setSendEmail(e.target.checked)}
              />
              E-postayla da gönder {contact.email ? `(${contact.email})` : "(e-posta yok)"}
            </label>
          </div>
          <DialogFooter>
            <Button variant="ghost" onClick={() => onOpenChange(false)}>
              Vazgeç
            </Button>
            <Button
              onClick={() => {
                setSubmitted(true);
                if (!validateMoney(amount)) create.mutate();
              }}
              disabled={create.isPending}
            >
              {create.isPending ? "Oluşturuluyor…" : "Link oluştur"}
            </Button>
          </DialogFooter>
        </>
      )}
    </DialogShell>
  );
}

/** "Ödedim" diyen (veya bekleyen) linki onaylama: paranın girdiği hesap ve tutar zorunlu. */
export function ConfirmPaymentDialog({
  request,
  open,
  onOpenChange,
}: {
  request: PaymentRequest;
  open: boolean;
  onOpenChange: (v: boolean) => void;
}) {
  const refresh = useFinanceRefresh();
  const accounts = useCashAccounts(open);
  const [amount, setAmount] = useState(moneyToInput(request.amount));
  const [date, setDate] = useState(todayIstanbul());
  const [accountId, setAccountId] = useState("");
  const [submitted, setSubmitted] = useState(false);
  const selected = accountId || accounts.data?.find((a) => a.type === "Bank")?.id || "";
  const amountError = submitted ? validateMoney(amount) : null;

  const confirm = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/payment-requests/{id}/confirm", {
          params: { path: { id: request.id } },
          body: { cashAccountId: selected, amount: amountOf(amount), date },
        }),
      ),
    onSuccess: async () => {
      toast.success("Ödeme onaylandı; tahsilat kaydedildi.");
      await refresh();
      onOpenChange(false);
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  return (
    <DialogShell
      open={open}
      onOpenChange={onOpenChange}
      title="Ödemeyi onayla"
      description={`${request.contactName} · Hesaba geçtiğini gördüyseniz onaylayın.`}
    >
      <div className="flex flex-col gap-4">
        <MoneyField label="Gelen tutar (TL)" value={amount} onChange={setAmount} error={amountError} />
        <div className="grid grid-cols-2 gap-3">
          <Field label="Nereye girdi?">
            <select className={selectClass} value={selected} onChange={(e) => setAccountId(e.target.value)}>
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
      </div>
      <DialogFooter>
        <Button variant="ghost" onClick={() => onOpenChange(false)}>
          Vazgeç
        </Button>
        <Button
          onClick={() => {
            setSubmitted(true);
            if (!validateMoney(amount) && selected) confirm.mutate();
          }}
          disabled={confirm.isPending || !selected}
        >
          {confirm.isPending ? "Onaylanıyor…" : "Onayla"}
        </Button>
      </DialogFooter>
    </DialogShell>
  );
}

/**
 * Linki tekrar paylaş: geçerli link aynen gösterilir (müşteriye gönderilen link çalışmaya devam eder); süresi dolmuşsa
 * yenisi üretilir. "Linki yenile" eski linki bilerek geçersiz kılar, onay ister.
 */
export function SharePaymentDialog({
  request,
  open,
  onOpenChange,
}: {
  request: PaymentRequest;
  open: boolean;
  onOpenChange: (v: boolean) => void;
}) {
  const refresh = useFinanceRefresh();
  const [link, setLink] = useState<{ url: string; whatsappUrl: string; expiresAt: string; isNewLink: boolean } | null>(
    null,
  );
  const [confirmRenew, setConfirmRenew] = useState(false);
  const share = useMutation({
    mutationFn: (action: "share" | "renew") =>
      unwrap(
        action === "share"
          ? api.POST("/api/payment-requests/{id}/share", {
              params: { path: { id: request.id } },
              body: { sendEmail: false },
            })
          : api.POST("/api/payment-requests/{id}/renew", {
              params: { path: { id: request.id } },
              body: { sendEmail: false },
            }),
      ),
    onSuccess: async (r, action) => {
      setLink({ url: r.url, whatsappUrl: r.whatsappUrl, expiresAt: r.request.expiresAt, isNewLink: r.isNewLink });
      setConfirmRenew(false);
      if (action === "renew") toast.success("Link yenilendi; eski link artık çalışmıyor.");
      if (r.isNewLink) await refresh();
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  // Diyalog açılınca mevcut link istenir (StrictMode'da iki kez çalışmasın).
  const requested = useRef(false);
  const { mutate } = share;
  useEffect(() => {
    if (open && !requested.current) {
      requested.current = true;
      mutate("share");
    }
  }, [open, mutate]);

  return (
    <DialogShell
      open={open}
      onOpenChange={onOpenChange}
      title="Linki paylaş"
      description={`${request.contactName} · ${formatMoney(request.amount)}`}
    >
      {link ? (
        <div className="flex flex-col gap-4">
          {link.isNewLink && share.variables === "share" && (
            <Notice>Yeni link oluşturuldu; müşteriye daha önce gönderilen link artık çalışmıyor. Bu linki gönderin.</Notice>
          )}
          <ShareButtons url={link.url} whatsappUrl={link.whatsappUrl} />
          <p className="text-sm text-neutral-600">{formatDate(link.expiresAt)} tarihine kadar geçerli.</p>
          {confirmRenew ? (
            <div className="flex flex-col gap-3 border-t border-border pt-4">
              <Notice>
                Yeni bir link oluşturulur ve süre 30 gün uzar. Müşteriye daha önce gönderdiğiniz link çalışmaz. Emin
                misiniz?
              </Notice>
              <div className="grid grid-cols-2 gap-2">
                <Button variant="outline" onClick={() => setConfirmRenew(false)} disabled={share.isPending}>
                  Vazgeç
                </Button>
                <Button onClick={() => share.mutate("renew")} disabled={share.isPending}>
                  {share.isPending ? "Yenileniyor…" : "Evet, yenile"}
                </Button>
              </div>
            </div>
          ) : (
            <Button variant="ghost" className="self-start" onClick={() => setConfirmRenew(true)}>
              Linki yenile
            </Button>
          )}
        </div>
      ) : share.isError ? (
        <div className="flex flex-col gap-3">
          <Notice>{errorMessage(share.error)}</Notice>
          <Button onClick={() => share.mutate("share")}>Tekrar dene</Button>
        </div>
      ) : (
        <p role="status" className="text-sm text-neutral-600">
          Link hazırlanıyor…
        </p>
      )}
    </DialogShell>
  );
}
