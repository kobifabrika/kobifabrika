"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { cn } from "cn";
import { Check, ChevronDown, Plus } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { Controller, useForm, useWatch } from "react-hook-form";
import { toast } from "sonner";
import { z } from "zod";

import { contactHref, contactsKey } from "@/components/contacts/contact-list";
import { selectClass, tagColorClass } from "@/components/contacts/contact-bits";
import { useLocations } from "@/components/business/business-fields";
import { Field, FormMessage } from "@/components/forms/field";
import { useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";
import { ApiError, api, errorMessage, fieldErrors, unwrap } from "@/lib/api/client";
import {
  sourceNames,
  tagColorNames,
  type Contact,
  type ContactRequest,
  type ContactSource,
  type TagColor,
} from "@/lib/api/types";
import { emailSchema, formatPhone, moneySchema, normalizePhone, PHONE_MESSAGE } from "@/lib/validation/rules";
import { can } from "@/lib/permissions";
import { todayIstanbul } from "@/lib/format";

const optional = (max: number, message: string) => z.string().trim().max(max, message);

const schema = z
  .object({
    isCustomer: z.boolean(),
    isSupplier: z.boolean(),
    firstName: optional(100, "Ad en fazla 100 karakter olabilir."),
    lastName: optional(100, "Soyad en fazla 100 karakter olabilir."),
    companyName: optional(200, "Firma adı en fazla 200 karakter olabilir."),
    phone: z.string().refine((v) => !v.trim() || normalizePhone(v) !== null, PHONE_MESSAGE),
    email: z.string().refine((v) => !v.trim() || emailSchema.safeParse(v).success, "Geçerli bir e-posta adresi girin."),
    birthDate: z
      .string()
      .refine((v) => !v || (v >= "1900-01-01" && v <= todayIstanbul()), "Geçerli bir doğum tarihi girin."),
    address: optional(500, "Adres en fazla 500 karakter olabilir."),
    city: z.string(),
    district: z.string(),
    source: z.string(),
    notes: optional(2000, "Not en fazla 2000 karakter olabilir."),
    taxNumber: z
      .string()
      .trim()
      .refine((v) => !v || /^[0-9]{10,11}$/.test(v), "Vergi/TC kimlik no 10 veya 11 rakam olmalı."),
    taxOffice: optional(100, "Vergi dairesi en fazla 100 karakter olabilir."),
    tagIds: z.array(z.string()),
    sms: z.boolean(),
    emailConsent: z.boolean(),
    call: z.boolean(),
    openingAmount: z.string(),
    openingDirection: z.enum(["Receivable", "Payable"]),
  })
  .superRefine((v, ctx) => {
    if (!v.firstName && !v.lastName && !v.companyName)
      ctx.addIssue({ code: "custom", path: ["firstName"], message: "Ad veya firma adı girin." });
    if (!v.isCustomer && !v.isSupplier)
      ctx.addIssue({ code: "custom", path: ["isCustomer"], message: "Kaydın müşteri mi tedarikçi mi olduğunu seçin." });
    if (v.openingAmount.trim()) {
      const parsed = moneySchema.safeParse(v.openingAmount);
      if (!parsed.success)
        ctx.addIssue({ code: "custom", path: ["openingAmount"], message: parsed.error.issues[0].message });
    }
  });

type Values = z.infer<typeof schema>;

function toValues(c: Contact | undefined, supplier: boolean): Values {
  return {
    isCustomer: c ? c.isCustomer : !supplier,
    isSupplier: c ? c.isSupplier : supplier,
    firstName: c?.firstName ?? "",
    lastName: c?.lastName ?? "",
    companyName: c?.companyName ?? "",
    phone: c?.phone ? `0${formatPhone(c.phone)}` : "",
    email: c?.email ?? "",
    birthDate: c?.birthDate ?? "",
    address: c?.address ?? "",
    city: c?.city ?? "",
    district: c?.district ?? "",
    source: c?.source ?? "",
    notes: c?.notes ?? "",
    taxNumber: c?.taxNumber ?? "",
    taxOffice: c?.taxOffice ?? "",
    tagIds: c?.tags.map((t) => t.id) ?? [],
    sms: c?.consents.find((x) => x.channel === "Sms")?.granted ?? false,
    emailConsent: c?.consents.find((x) => x.channel === "Email")?.granted ?? false,
    call: c?.consents.find((x) => x.channel === "Call")?.granted ?? false,
    openingAmount: "",
    openingDirection: "Receivable",
  };
}

type PhoneConflict = { id: string; name: string; isCustomer: boolean };

/** Yeni kayıt veya düzenleme. Telefon yazılırken işletmede başka kayıtta olup olmadığı kontrol edilir. */
export function ContactForm({ contact, supplier = false }: { contact?: Contact; supplier?: boolean }) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const { role } = useSession();
  const [error, setError] = useState<string | null>(null);
  const [serverConflict, setServerConflict] = useState<PhoneConflict | null>(null);
  const [showTax, setShowTax] = useState(!!(contact?.taxNumber || contact?.taxOffice));
  const locations = useLocations();
  const form = useForm<Values>({ resolver: zodResolver(schema), defaultValues: toValues(contact, supplier) });
  const { register, control, formState } = form;
  const errors = formState.errors;
  const phone = useWatch({ control, name: "phone" });
  const city = useWatch({ control, name: "city" });
  const districts = locations.data?.find((l) => l.name === city)?.districts ?? [];
  const isNew = !contact;
  const canOpening = isNew && can(role, "manageFinance");

  // Telefon geçerli hale gelince başka kayıtta var mı diye sorulur.
  const normalized = normalizePhone(phone);
  const phoneCheck = useQuery({
    queryKey: ["contacts", "phone-check", normalized, contact?.id],
    queryFn: () =>
      unwrap(
        api.GET("/api/contacts/phone-check", { params: { query: { phone: normalized!, excludeId: contact?.id } } }),
      ),
    enabled: !!normalized,
    staleTime: 10_000,
  });
  const checked = phoneCheck.data;
  const conflict: PhoneConflict | null =
    serverConflict ??
    (normalized && checked?.existingContactId
      ? { id: checked.existingContactId, name: checked.existingContactName ?? "", isCustomer: true }
      : null);

  const onSubmit = form.handleSubmit(async (v) => {
    setError(null);
    setServerConflict(null);
    const body: ContactRequest = {
      firstName: v.firstName || null,
      lastName: v.lastName || null,
      companyName: v.companyName || null,
      isCustomer: v.isCustomer,
      isSupplier: v.isSupplier,
      phone: v.phone.trim() || null,
      email: v.email.trim() || null,
      birthDate: v.birthDate || null,
      address: v.address || null,
      city: v.city || null,
      district: v.district || null,
      source: (v.source || null) as ContactSource | null,
      notes: v.notes || null,
      taxNumber: v.taxNumber || null,
      taxOffice: v.taxOffice || null,
      tagIds: v.tagIds,
      consents: { sms: v.sms, email: v.emailConsent, call: v.call },
      openingBalance:
        canOpening && v.openingAmount.trim()
          ? { amount: moneySchema.parse(v.openingAmount), direction: v.openingDirection, date: null }
          : null,
    };
    try {
      const saved = isNew
        ? await unwrap(api.POST("/api/contacts", { body }))
        : await unwrap(api.PUT("/api/contacts/{id}", { params: { path: { id: contact.id } }, body }));
      await queryClient.invalidateQueries({ queryKey: contactsKey });
      toast.success(isNew ? `${saved.displayName} eklendi.` : "Kart güncellendi.");
      router.push(contactHref(saved));
    } catch (e) {
      if (e instanceof ApiError && e.status === 409 && (e.problem as { code?: string }).code === "phone_taken") {
        const p = e.problem as { contactId?: string; contactName?: string };
        setServerConflict({ id: p.contactId!, name: p.contactName ?? "", isCustomer: true });
        form.setError("phone", { message: e.message });
        return;
      }
      const fields = fieldErrors(e);
      Object.entries(fields).forEach(([k, m]) =>
        form.setError(k.replace("openingBalance.amount", "openingAmount") as keyof Values, { message: m }),
      );
      if (Object.keys(fields).length === 0) setError(errorMessage(e));
    }
  });

  return (
    <form className="flex max-w-2xl flex-col gap-6" onSubmit={onSubmit} noValidate>
      <fieldset className="flex flex-col gap-2">
        <legend className="mb-2 text-sm font-semibold text-black">Kayıt türü</legend>
        <div className="flex flex-wrap gap-x-6">
          <Controller
            control={control}
            name="isCustomer"
            render={({ field }) => (
              <label className="flex min-h-11 items-center gap-3 text-base text-black">
                <Checkbox checked={field.value} onCheckedChange={(c) => field.onChange(c === true)} />
                Müşteri
              </label>
            )}
          />
          <Controller
            control={control}
            name="isSupplier"
            render={({ field }) => (
              <label className="flex min-h-11 items-center gap-3 text-base text-black">
                <Checkbox checked={field.value} onCheckedChange={(c) => field.onChange(c === true)} />
                Tedarikçi
              </label>
            )}
          />
        </div>
        {errors.isCustomer && (
          <p role="alert" className="text-sm text-danger-600">
            {errors.isCustomer.message}
          </p>
        )}
      </fieldset>

      <div className="grid gap-5 sm:grid-cols-2">
        <Field label="Ad" error={errors.firstName?.message}>
          <Input autoComplete="off" autoFocus={isNew} {...register("firstName")} />
        </Field>
        <Field label="Soyad" error={errors.lastName?.message}>
          <Input autoComplete="off" {...register("lastName")} />
        </Field>
      </div>
      <Field label="Firma adı" hint="Ad veya firma adından biri yeterli." error={errors.companyName?.message}>
        <Input autoComplete="off" {...register("companyName")} />
      </Field>

      <div className="grid gap-5 sm:grid-cols-2">
        <div className="flex flex-col gap-2">
          <Field label="Telefon" hint="Önerilir; WhatsApp ve SMS için gerekli." error={errors.phone?.message}>
            <Input type="tel" inputMode="tel" autoComplete="off" placeholder="0532 123 45 67" {...register("phone")} />
          </Field>
          {conflict && !errors.phone && (
            <p role="alert" className="text-sm text-black">
              Bu numara <span className="font-semibold">{conflict.name}</span> adına kayıtlı.{" "}
              <Link
                href={contactHref(conflict)}
                className="font-semibold underline decoration-brand decoration-2 underline-offset-2"
              >
                Karta git
              </Link>
            </p>
          )}
          {conflict && errors.phone && (
            <Link
              href={contactHref(conflict)}
              className="text-sm font-semibold text-black underline decoration-brand decoration-2 underline-offset-2"
            >
              {conflict.name} kartına git
            </Link>
          )}
        </div>
        <Field label="E-posta" error={errors.email?.message}>
          <Input type="email" inputMode="email" autoComplete="off" {...register("email")} />
        </Field>
      </div>

      <div className="grid gap-5 sm:grid-cols-2">
        <Field label="Doğum günü" error={errors.birthDate?.message}>
          <Input type="date" max={todayIstanbul()} {...register("birthDate")} />
        </Field>
        <Field label="Bizi nereden buldu?">
          <select className={selectClass} {...register("source")}>
            <option value="">Seçin</option>
            {Object.entries(sourceNames).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </Field>
      </div>

      <Controller
        control={control}
        name="tagIds"
        render={({ field }) => <TagPicker value={field.value} onChange={field.onChange} />}
      />

      <div className="grid gap-5 sm:grid-cols-2">
        <Field label="İl">
          <select
            className={selectClass}
            disabled={locations.isPending}
            {...register("city", { onChange: () => form.setValue("district", "") })}
          >
            <option value="">{locations.isPending ? "Yükleniyor…" : "Seçin"}</option>
            {locations.data?.map((l) => (
              <option key={l.code} value={l.name}>
                {l.name}
              </option>
            ))}
          </select>
        </Field>
        <Field label="İlçe">
          <select className={selectClass} disabled={!city} {...register("district")}>
            <option value="">{city ? "Seçin" : "Önce il seçin"}</option>
            {districts.map((d) => (
              <option key={d} value={d}>
                {d}
              </option>
            ))}
          </select>
        </Field>
      </div>
      <Field label="Adres" error={errors.address?.message}>
        <Textarea rows={2} {...register("address")} />
      </Field>
      <Field
        label="Kısa not"
        hint="Kartın üstünde görünür. Tarihli notlar için kartta Notlar sekmesi var."
        error={errors.notes?.message}
      >
        <Textarea rows={2} {...register("notes")} />
      </Field>

      <fieldset className="flex flex-col gap-1 border-t border-border pt-5">
        <legend className="text-sm font-semibold text-black">İletişim izinleri (KVKK)</legend>
        <p className="mb-2 text-sm text-neutral-600">
          Sadece müşteriden izin aldıysanız açın. İzni olmayana kampanya mesajı gönderilmez.
        </p>
        {(
          [
            ["sms", "SMS ile kampanya ve bilgilendirme"],
            ["emailConsent", "E-posta ile kampanya ve bilgilendirme"],
            ["call", "Telefonla arama"],
          ] as const
        ).map(([name, label]) => (
          <Controller
            key={name}
            control={control}
            name={name}
            render={({ field }) => (
              <label className="flex min-h-11 items-center justify-between gap-4 text-base text-black">
                {label}
                <Switch checked={field.value} onCheckedChange={field.onChange} />
              </label>
            )}
          />
        ))}
      </fieldset>

      <div className="border-t border-border pt-5">
        <button
          type="button"
          onClick={() => setShowTax((s) => !s)}
          aria-expanded={showTax}
          className="flex min-h-11 items-center gap-2 text-sm font-semibold text-black"
        >
          <ChevronDown aria-hidden className={cn("size-4 transition-transform", showTax && "rotate-180")} />
          Fatura bilgileri
        </button>
        {showTax && (
          <div className="mt-3 grid gap-5 sm:grid-cols-2">
            <Field label="Vergi / TC kimlik no" error={errors.taxNumber?.message}>
              <Input inputMode="numeric" {...register("taxNumber")} />
            </Field>
            <Field label="Vergi dairesi" error={errors.taxOffice?.message}>
              <Input {...register("taxOffice")} />
            </Field>
          </div>
        )}
      </div>

      {canOpening && (
        <fieldset className="flex flex-col gap-3 border-t border-border pt-5">
          <legend className="text-sm font-semibold text-black">Açılış bakiyesi (isteğe bağlı)</legend>
          <p className="text-sm text-neutral-600">Defterden devreden borç/alacak varsa girin.</p>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Tutar (TL)" error={errors.openingAmount?.message}>
              <Input inputMode="decimal" placeholder="0,00" {...register("openingAmount")} />
            </Field>
            <Field label="Kim kime borçlu?">
              <select className={selectClass} {...register("openingDirection")}>
                <option value="Receivable">Bize borçlu (Alacak)</option>
                <option value="Payable">Biz borçluyuz (Borç)</option>
              </select>
            </Field>
          </div>
        </fieldset>
      )}

      {error && <FormMessage>{error}</FormMessage>}
      <div className="flex gap-2">
        <Button type="submit" size="lg" disabled={formState.isSubmitting}>
          {formState.isSubmitting ? "Kaydediliyor…" : "Kaydet"}
        </Button>
        <Button type="button" size="lg" variant="ghost" onClick={() => router.back()}>
          Vazgeç
        </Button>
      </div>
    </form>
  );
}

/** Etiket seçimi: mevcut etiketler aç/kapa çip; yeni etiket adı + renkle eklenir. */
export function TagPicker({ value, onChange }: { value: string[]; onChange: (ids: string[]) => void }) {
  const queryClient = useQueryClient();
  const tags = useQuery({ queryKey: ["tags"], queryFn: () => unwrap(api.GET("/api/tags")) });
  const [name, setName] = useState("");
  const [color, setColor] = useState<TagColor>("Gray");
  const [busy, setBusy] = useState(false);

  const add = async () => {
    const trimmed = name.trim();
    if (!trimmed) return;
    const existing = tags.data?.find((t) => t.name.toLocaleLowerCase("tr-TR") === trimmed.toLocaleLowerCase("tr-TR"));
    if (existing) {
      if (!value.includes(existing.id)) onChange([...value, existing.id]);
      setName("");
      return;
    }
    setBusy(true);
    try {
      const tag = await unwrap(api.POST("/api/tags", { body: { name: trimmed, color } }));
      await queryClient.invalidateQueries({ queryKey: ["tags"] });
      onChange([...value, tag.id]);
      setName("");
    } catch (e) {
      toast.error(errorMessage(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <fieldset className="flex flex-col gap-2">
      <legend className="mb-1 text-sm font-semibold text-black">Etiketler</legend>
      {(tags.data?.length ?? 0) > 0 && (
        <div className="flex flex-wrap gap-2">
          {tags.data!.map((t) => {
            const on = value.includes(t.id);
            return (
              <button
                key={t.id}
                type="button"
                aria-pressed={on}
                onClick={() => onChange(on ? value.filter((v) => v !== t.id) : [...value, t.id])}
                className={cn(
                  "inline-flex min-h-11 items-center gap-1 rounded-full border px-3 text-sm font-medium",
                  on ? tagColorClass[t.color] : "border-border bg-background text-neutral-700 hover:border-black",
                  on && t.color === "Gray" && "border-black",
                )}
              >
                {on && <Check aria-hidden className="size-3.5" />}
                {t.name}
              </button>
            );
          })}
        </div>
      )}
      <div className="flex flex-wrap gap-2">
        <Input
          value={name}
          onChange={(e) => setName(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter") {
              e.preventDefault();
              void add();
            }
          }}
          placeholder="Yeni etiket (ör. VIP)"
          maxLength={50}
          aria-label="Yeni etiket adı"
          className="min-w-0 flex-1 basis-full sm:basis-0"
        />
        <select
          className={cn(selectClass, "w-auto flex-1 sm:w-32 sm:flex-none")}
          value={color}
          onChange={(e) => setColor(e.target.value as TagColor)}
          aria-label="Etiket rengi"
        >
          {Object.entries(tagColorNames).map(([v, label]) => (
            <option key={v} value={v}>
              {label}
            </option>
          ))}
        </select>
        <Button type="button" variant="outline" onClick={add} disabled={!name.trim() || busy}>
          <Plus aria-hidden />
          Ekle
        </Button>
      </div>
    </fieldset>
  );
}

/** Düzenleme sayfası: kartı yükler, formu doldurur. */
export function EditContact({ id }: { id: string }) {
  const contact = useQuery({
    queryKey: [...contactsKey, "detail", id],
    queryFn: () => unwrap(api.GET("/api/contacts/{id}", { params: { path: { id } } })),
  });
  if (contact.isPending)
    return (
      <p role="status" className="text-sm text-neutral-600">
        Yükleniyor…
      </p>
    );
  if (contact.isError) return <FormMessage>{errorMessage(contact.error)}</FormMessage>;
  return <ContactForm contact={contact.data} />;
}
