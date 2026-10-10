"use client";

import { useQuery } from "@tanstack/react-query";
import { cn } from "cn";
import { Controller, type Control, type FieldErrors, type UseFormRegister, useWatch } from "react-hook-form";
import { z } from "zod";

import { PhoneInput } from "@/components/auth/auth-helpers";
import { sectors } from "@/components/business/sectors";
import { Field } from "@/components/forms/field";
import { Input } from "@/components/ui/input";
import { api, unwrap } from "@/lib/api/client";
import type { Sector } from "@/lib/api/types";
import { phoneSchema } from "@/lib/validation/rules";

export const businessBasicsSchema = z.object({
  name: z.string().trim().min(2, "İşletme adını girin.").max(200, "İşletme adı en fazla 200 karakter olabilir."),
  sector: z.custom<Sector>((v) => typeof v === "string" && v.length > 0, "Sektör seçin."),
  phone: phoneSchema,
  city: z.string().min(1, "İl seçin."),
  district: z.string().min(1, "İlçe seçin."),
});

export type BusinessBasics = z.infer<typeof businessBasicsSchema>;

/** 81 il ve ilçeleri; bir kez yüklenir. */
export function useLocations() {
  return useQuery({
    queryKey: ["locations"],
    queryFn: () => unwrap(api.GET("/api/locations")),
    staleTime: Infinity,
  });
}

const selectClass =
  "h-11 w-full rounded-lg border border-input bg-background px-3 text-base text-brand-900 outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring aria-invalid:border-destructive disabled:bg-neutral-100 disabled:text-neutral-600";

type Props<T extends BusinessBasics> = {
  control: Control<T>;
  register: UseFormRegister<T>;
  errors: FieldErrors<BusinessBasics>;
  /** İl değişince ilçeyi sıfırlamak için. */
  onCityChange: () => void;
  disabled?: boolean;
};

/** İşletme adı, sektör kartları, telefon, il/ilçe. Kurulum 1. adım ve Ayarlar'da ortak. */
export function BusinessFields<T extends BusinessBasics>({ control, register, errors, onCityChange, disabled }: Props<T>) {
  // Ortak alanlar BusinessBasics'te; genel form tipi T bu alanları içerir.
  const c = control as unknown as Control<BusinessBasics>;
  const r = register as unknown as UseFormRegister<BusinessBasics>;
  const locations = useLocations();
  const city = useWatch({ control: c, name: "city" });
  const districts = locations.data?.find((l) => l.name === city)?.districts ?? [];

  return (
    <div className="flex flex-col gap-5">
      <Field label="İşletme adı" error={errors.name?.message}>
        <Input autoComplete="organization" placeholder="ör. Şık Kuaför" disabled={disabled} {...r("name")} />
      </Field>

      <fieldset className="flex flex-col gap-2" disabled={disabled}>
        <legend className="mb-2 text-sm font-semibold text-brand-900">Sektör</legend>
        <Controller
          control={c}
          name="sector"
          render={({ field }) => (
            <div role="radiogroup" aria-label="Sektör" className="grid grid-cols-2 gap-2 sm:grid-cols-4">
              {sectors.map(({ value, label, icon: Icon }) => {
                const selected = field.value === value;
                return (
                  <button
                    key={value}
                    type="button"
                    role="radio"
                    aria-checked={selected}
                    onClick={() => field.onChange(value)}
                    className={cn(
                      "flex min-h-20 flex-col items-start justify-center gap-2 rounded-lg border px-3 py-2 text-left text-sm font-semibold transition-colors",
                      selected
                        ? "border-brand-500 bg-brand-500 text-white"
                        : "border-border bg-white text-brand-900 hover:border-brand-500",
                    )}
                  >
                    <Icon aria-hidden className={cn("size-6", selected ? "text-white" : "text-brand-500")} />
                    {label}
                  </button>
                );
              })}
            </div>
          )}
        />
        {errors.sector?.message && (
          <p role="alert" className="text-sm text-danger-600">
            {errors.sector.message}
          </p>
        )}
      </fieldset>

      <Field label="İşletme telefonu" error={errors.phone?.message}>
        <PhoneInput autoComplete="tel-national" placeholder="212 555 44 33" disabled={disabled} {...r("phone")} />
      </Field>

      <div className="grid gap-5 sm:grid-cols-2">
        {/* Liste geç yüklendiği için seçimler kontrollü (Controller): kayıtlı il/ilçe seçenekler gelince görünür. */}
        <Controller
          control={c}
          name="city"
          render={({ field }) => (
            <Field label="İl" error={errors.city?.message}>
              <select
                className={selectClass}
                disabled={disabled || locations.isPending}
                name={field.name}
                ref={field.ref}
                value={field.value}
                onBlur={field.onBlur}
                onChange={(e) => {
                  field.onChange(e.target.value);
                  onCityChange();
                }}
              >
                <option value="">{locations.isPending ? "Yükleniyor…" : "İl seçin"}</option>
                {locations.data?.map((l) => (
                  <option key={l.code} value={l.name}>
                    {l.name}
                  </option>
                ))}
              </select>
            </Field>
          )}
        />
        <Controller
          control={c}
          name="district"
          render={({ field }) => (
            <Field label="İlçe" error={errors.district?.message}>
              <select
                className={selectClass}
                disabled={disabled || !city || locations.isPending}
                name={field.name}
                ref={field.ref}
                value={field.value}
                onBlur={field.onBlur}
                onChange={(e) => field.onChange(e.target.value)}
              >
                <option value="">{city ? "İlçe seçin" : "Önce il seçin"}</option>
                {districts.map((d) => (
                  <option key={d} value={d}>
                    {d}
                  </option>
                ))}
              </select>
            </Field>
          )}
        />
      </div>
    </div>
  );
}
