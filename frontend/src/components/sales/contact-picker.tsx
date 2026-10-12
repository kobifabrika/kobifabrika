"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import { Search, UserPlus, X } from "lucide-react";
import { useId, useState } from "react";

import { useDebounced } from "@/components/catalog/product-list";
import { Field } from "@/components/forms/field";
import { useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { api, errorMessage, fieldErrors, unwrap } from "@/lib/api/client";
import { displayPhone } from "@/lib/contacts";
import type { CartContact } from "@/lib/cart";
import { can } from "@/lib/permissions";
import { normalizePhone, PHONE_MESSAGE } from "@/lib/validation/rules";

/**
 * Müşteri seçimi (satış, teklif, tahsilat): ad veya telefonla arama, seçili müşteri, sepetten çıkmadan hızlı yeni müşteri.
 * `required` değilse "Müşterisiz" de geçerli bir seçim. `kind="supplier"`: tedarikçi arar (ödeme, gider), hızlı ekleme yok.
 */
export function ContactPicker({
  value,
  onChange,
  required = false,
  error,
  kind = "customer",
}: {
  value: CartContact | null;
  onChange: (contact: CartContact | null) => void;
  required?: boolean;
  error?: string | null;
  kind?: "customer" | "supplier";
}) {
  const title = kind === "supplier" ? "Tedarikçi" : "Müşteri";
  const { role } = useSession();
  const [text, setText] = useState("");
  const [creating, setCreating] = useState(false);
  const inputId = useId();
  const debounced = useDebounced(text.trim(), 250);
  const results = useQuery({
    queryKey: ["contacts", "picker", kind, debounced],
    queryFn: () => unwrap(api.GET("/api/contacts", { params: { query: { search: debounced, type: kind, pageSize: 6 } } })),
    enabled: debounced.length >= 2 && !value,
  });

  if (value)
    return (
      <div className="flex flex-col gap-2">
        <span className="text-sm font-semibold text-black">{title}</span>
        <div className="flex min-h-11 items-center justify-between gap-2 border border-black px-3 py-2">
          <span className="flex min-w-0 flex-col">
            <span className="truncate text-sm font-semibold text-black">{value.name}</span>
            {value.phone && <span className="text-xs text-neutral-700 tabular-nums">{displayPhone(value.phone)}</span>}
          </span>
          <Button type="button" variant="ghost" size="icon" aria-label={`${title} seçimini kaldır`} onClick={() => onChange(null)}>
            <X aria-hidden />
          </Button>
        </div>
      </div>
    );

  if (creating) return <QuickContact onCreated={(c) => (setCreating(false), onChange(c))} onCancel={() => setCreating(false)} initialName={text} />;

  const items = debounced.length >= 2 ? (results.data?.items ?? []) : [];
  return (
    <div className="flex flex-col gap-2">
      <label htmlFor={inputId} className="text-sm font-semibold text-black">
        {title} {!required && <span className="font-normal text-neutral-600">(isteğe bağlı)</span>}
      </label>
      <div className="relative">
        <Search aria-hidden className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-neutral-600" />
        <Input
          id={inputId}
          type="search"
          value={text}
          onChange={(e) => setText(e.target.value)}
          placeholder="Ad veya telefon ara"
          autoComplete="off"
          aria-invalid={error ? true : undefined}
          className="pl-9"
        />
      </div>
      {error && (
        <p role="alert" className="text-sm text-danger-600">
          {error}
        </p>
      )}
      {items.length > 0 && (
        <ul className="flex flex-col divide-y divide-border border border-border">
          {items.map((c) => (
            <li key={c.id}>
              <button
                type="button"
                onClick={() => {
                  onChange({ id: c.id, name: c.displayName, phone: c.phone });
                  setText("");
                }}
                className="flex min-h-11 w-full flex-col items-start justify-center px-3 py-2 text-left hover:bg-neutral-50"
              >
                <span className="text-sm font-semibold text-black">{c.displayName}</span>
                {c.phone && <span className="text-xs text-neutral-700 tabular-nums">{displayPhone(c.phone)}</span>}
              </button>
            </li>
          ))}
        </ul>
      )}
      {debounced.length >= 2 && results.isSuccess && items.length === 0 && (
        <p className="text-sm text-neutral-600">Bu adla {title.toLocaleLowerCase("tr-TR")} yok.</p>
      )}
      {kind === "customer" && can(role, "writeContacts") && (
        <Button type="button" variant="ghost" className="self-start" onClick={() => setCreating(true)}>
          <UserPlus aria-hidden />
          Yeni müşteri
        </Button>
      )}
    </div>
  );
}

/** Satıştan çıkmadan müşteri ekleme: ad soyad ve (isteğe bağlı) telefon. */
function QuickContact({
  onCreated,
  onCancel,
  initialName,
}: {
  onCreated: (c: CartContact) => void;
  onCancel: () => void;
  initialName: string;
}) {
  const [name, setName] = useState(/\d/.test(initialName) ? "" : initialName);
  const [phone, setPhone] = useState(/\d/.test(initialName) ? initialName : "");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const create = useMutation({
    mutationFn: () => {
      const [first, ...rest] = name.trim().split(/\s+/);
      return unwrap(
        api.POST("/api/contacts", {
          body: {
            firstName: first,
            lastName: rest.join(" ") || null,
            companyName: null,
            isCustomer: true,
            isSupplier: false,
            phone: phone.trim() || null,
            email: null,
            birthDate: null,
            address: null,
            city: null,
            district: null,
            source: null,
            notes: null,
            taxNumber: null,
            taxOffice: null,
            tagIds: null,
            consents: null,
            openingBalance: null,
          },
        }),
      );
    },
    onSuccess: (c) => onCreated({ id: c.id, name: c.displayName, phone: c.phone }),
    onError: (e) => {
      const fields = fieldErrors(e);
      setErrors(Object.keys(fields).length ? fields : { name: errorMessage(e) });
    },
  });

  return (
    <div className="flex flex-col gap-3 border border-border p-3">
      <span className="text-sm font-semibold text-black">Yeni müşteri</span>
      <Field label="Ad soyad" error={errors.name ?? errors.firstName}>
        <Input value={name} onChange={(e) => setName(e.target.value)} autoComplete="off" />
      </Field>
      <Field label="Telefon (isteğe bağlı)" error={errors.phone}>
        <Input type="tel" inputMode="tel" value={phone} onChange={(e) => setPhone(e.target.value)} placeholder="0532 123 45 67" />
      </Field>
      <div className="grid grid-cols-2 gap-2">
        <Button type="button" variant="outline" onClick={onCancel}>
          Vazgeç
        </Button>
        <Button
          type="button"
          disabled={create.isPending}
          onClick={() => {
            const next: Record<string, string> = {};
            if (name.trim().length < 2) next.name = "Ad soyad girin.";
            if (phone.trim() && !normalizePhone(phone)) next.phone = PHONE_MESSAGE;
            setErrors(next);
            if (Object.keys(next).length === 0) create.mutate();
          }}
        >
          {create.isPending ? "Ekleniyor…" : "Ekle ve seç"}
        </Button>
      </div>
    </div>
  );
}
