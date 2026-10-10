"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState, type ReactNode } from "react";
import { useForm } from "react-hook-form";
import { toast } from "sonner";
import { z } from "zod";

import { businessBasicsSchema, BusinessFields } from "@/components/business/business-fields";
import { LogoUpload } from "@/components/business/logo-upload";
import { templateNames } from "@/components/business/sectors";
import { Field, FormMessage } from "@/components/forms/field";
import { meQueryKey, useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { api, errorMessage, fieldErrors, unwrap } from "@/lib/api/client";
import type { Business } from "@/lib/api/types";
import { can } from "@/lib/permissions";
import { emailSchema, formatPhone, isValidSlug } from "@/lib/validation/rules";

const businessKey = ["business"] as const;

const schema = businessBasicsSchema.extend({
  email: z.union([z.literal(""), emailSchema]),
  address: z.string().max(500, "Adres en fazla 500 karakter olabilir."),
});

type Values = z.infer<typeof schema>;

export function BusinessSettings() {
  const { role } = useSession();
  const readOnly = !can(role, "manageBusiness");
  const business = useQuery({ queryKey: businessKey, queryFn: () => unwrap(api.GET("/api/business")) });

  if (business.isPending) return <p role="status" className="text-sm text-neutral-600">Yükleniyor…</p>;
  if (business.isError) return <FormMessage>{errorMessage(business.error)}</FormMessage>;

  return (
    <div className="flex flex-col gap-6">
      {readOnly && <FormMessage tone="info">Mali müşavir olarak ayarları sadece görüntüleyebilirsin.</FormMessage>}
      <InfoCard business={business.data} readOnly={readOnly} />
      <SlugCard business={business.data} readOnly={readOnly} />
      <ModulesCard business={business.data} readOnly={readOnly} />
      <SettingsCard title="Logo" description="Tekliflerde ve randevu sayfanda görünür.">
        <LogoSection business={business.data} readOnly={readOnly} />
      </SettingsCard>
    </div>
  );
}

function SettingsCard({ title, description, children }: { title: string; description?: string; children: ReactNode }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-black">{title}</CardTitle>
        {description && <CardDescription>{description}</CardDescription>}
      </CardHeader>
      <CardContent>{children}</CardContent>
    </Card>
  );
}

function useRefreshBusiness() {
  const queryClient = useQueryClient();
  return (next?: Business) => {
    if (next) queryClient.setQueryData(businessKey, next);
    return queryClient.invalidateQueries({ queryKey: meQueryKey });
  };
}

function InfoCard({ business, readOnly }: { business: Business; readOnly: boolean }) {
  const refresh = useRefreshBusiness();
  const [error, setError] = useState<string | null>(null);
  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: business.name,
      sector: business.sector,
      phone: formatPhone(business.phone),
      city: business.city ?? "",
      district: business.district ?? "",
      email: business.email ?? "",
      address: business.address ?? "",
    },
  });

  const onSubmit = form.handleSubmit(async (values) => {
    setError(null);
    try {
      await refresh(await unwrap(api.PUT("/api/business", { body: values })));
      form.reset(values);
      toast.success("İşletme bilgileri kaydedildi.");
    } catch (e) {
      const fields = fieldErrors(e);
      for (const [name, message] of Object.entries(fields)) {
        if (name in values) form.setError(name as keyof Values, { message });
      }
      if (!Object.keys(fields).length) setError(errorMessage(e));
    }
  });

  return (
    <SettingsCard
      title="İşletme bilgileri"
      description={`Şablon: ${templateNames[business.template]} — sektörü değiştirmek menü modüllerini değiştirmez.`}
    >
      <form className="flex flex-col gap-5" onSubmit={onSubmit} noValidate>
        <BusinessFields
          control={form.control}
          register={form.register}
          errors={form.formState.errors}
          onCityChange={() => form.setValue("district", "")}
          disabled={readOnly}
        />
        <Field label="E-posta (isteğe bağlı)" error={form.formState.errors.email?.message}>
          <Input type="email" inputMode="email" disabled={readOnly} {...form.register("email")} />
        </Field>
        <Field label="Adres (isteğe bağlı)" error={form.formState.errors.address?.message}>
          <Input autoComplete="street-address" disabled={readOnly} {...form.register("address")} />
        </Field>
        {error && <FormMessage>{error}</FormMessage>}
        {!readOnly && (
          <Button type="submit" size="lg" className="self-start" disabled={form.formState.isSubmitting || !form.formState.isDirty}>
            {form.formState.isSubmitting ? "Kaydediliyor…" : "Kaydet"}
          </Button>
        )}
      </form>
    </SettingsCard>
  );
}

function SlugCard({ business, readOnly }: { business: Business; readOnly: boolean }) {
  const refresh = useRefreshBusiness();
  const [slug, setSlug] = useState(business.slug);
  const [debounced, setDebounced] = useState(slug);

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(slug), 300);
    return () => clearTimeout(timer);
  }, [slug]);

  const changed = debounced !== business.slug;
  const availability = useQuery({
    queryKey: ["slug-availability", debounced],
    queryFn: () => unwrap(api.GET("/api/business/slug-availability", { params: { query: { slug: debounced } } })),
    enabled: !readOnly && changed && debounced.length > 0,
  });

  const save = useMutation({
    mutationFn: () => unwrap(api.PUT("/api/business/slug", { body: { slug } })),
    onSuccess: async (next) => {
      await refresh(next);
      toast.success("Randevu linki güncellendi.");
    },
  });

  const localInvalid = slug !== business.slug && !isValidSlug(slug);
  const message = localInvalid
    ? "3-60 karakter; sadece küçük harf (Türkçe karakter olmadan), rakam ve tire kullanın."
    : changed && availability.data && !availability.data.available
      ? (availability.data.message ?? "Bu adres kullanılamaz.")
      : undefined;
  const canSave = changed && !localInvalid && availability.data?.available === true && debounced === slug;

  return (
    <SettingsCard title="Randevu linki" description="Müşterilerin online randevu alacağı adres. Değiştirirsen eski link çalışmaz.">
      <div className="flex flex-col gap-4">
        <Field
          label="Adres"
          error={message ?? (save.isError ? errorMessage(save.error) : undefined)}
          hint={canSave ? "Bu adres uygun." : undefined}
        >
          <div className="flex items-stretch overflow-hidden rounded-lg border border-input focus-within:border-ring focus-within:ring-3 focus-within:ring-ring">
            <span className="flex items-center bg-neutral-100 px-3 text-sm text-neutral-600">kobifabrika.com/r/</span>
            <input
              value={slug}
              disabled={readOnly}
              onChange={(e) => setSlug(e.target.value.trim().toLowerCase())}
              className="h-11 min-w-0 flex-1 bg-background px-2 text-base text-black outline-none disabled:bg-neutral-100"
              autoCapitalize="none"
              autoCorrect="off"
              spellCheck={false}
            />
          </div>
        </Field>
        {!readOnly && (
          <Button className="self-start" size="lg" disabled={!canSave || save.isPending} onClick={() => save.mutate()}>
            {save.isPending ? "Kaydediliyor…" : "Linki kaydet"}
          </Button>
        )}
      </div>
    </SettingsCard>
  );
}

function ModulesCard({ business, readOnly }: { business: Business; readOnly: boolean }) {
  const refresh = useRefreshBusiness();
  const save = useMutation({
    mutationFn: (modules: { appointments: boolean; stock: boolean }) => unwrap(api.PUT("/api/business/modules", { body: modules })),
    onSuccess: async (next) => {
      await refresh(next);
      toast.success("Menü güncellendi.");
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  const rows = [
    { key: "appointments" as const, label: "Randevular", text: "Takvim, online randevu, hatırlatma SMS'i." },
    { key: "stock" as const, label: "Stok", text: "Stok hareketleri ve kritik stok uyarısı." },
  ];

  return (
    <SettingsCard
      title="Modüller"
      description="Şablonuna göre bazı modüller kapalı başlar; ihtiyacın varsa buradan açabilirsin."
    >
      <ul className="flex flex-col divide-y divide-border">
        {rows.map((row) => (
          <li key={row.key}>
            <label className="flex min-h-14 items-center justify-between gap-4 py-2">
              <span className="flex flex-col">
                <span className="text-sm font-semibold text-black">{row.label}</span>
                <span className="text-sm text-neutral-600">{row.text}</span>
              </span>
              <Switch
                checked={business.modules[row.key]}
                disabled={readOnly || save.isPending}
                onCheckedChange={(on) => save.mutate({ ...business.modules, [row.key]: on })}
              />
            </label>
          </li>
        ))}
      </ul>
    </SettingsCard>
  );
}

function LogoSection({ business, readOnly }: { business: Business; readOnly: boolean }) {
  const refresh = useRefreshBusiness();
  return (
    <LogoUpload
      logoUrl={business.logoUrl}
      endpoint="/api/business/logo"
      disabled={readOnly}
      onChange={(logoUrl) => {
        refresh({ ...business, logoUrl });
        toast.success(logoUrl ? "Logo güncellendi." : "Logo kaldırıldı.");
      }}
    />
  );
}
