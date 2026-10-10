"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { cn } from "cn";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { toast } from "sonner";

import { businessBasicsSchema, BusinessFields, type BusinessBasics } from "@/components/business/business-fields";
import { LogoUpload } from "@/components/business/logo-upload";
import { defaultWeek, invalidDays, WorkingHoursEditor } from "@/components/business/working-hours-editor";
import { FormMessage } from "@/components/forms/field";
import { meQueryKey, useMeQuery } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { api, ApiError, errorMessage, fieldErrors, unwrap } from "@/lib/api/client";
import type { components } from "@/lib/api/schema";
import type { WorkingHour } from "@/lib/api/types";
import { formatPhone } from "@/lib/validation/rules";

const steps = ["İşletme bilgileri", "Çalışma saatleri", "Logo"] as const;

/**
 * Kurulum sihirbazı (3 adım). Telefonda tek elle: tek sütun, büyük alanlar, altta sabit Geri/Devam.
 * Yarıda bırakılırsa 1. adım kayıtlıysa 2. adımdan devam eder.
 */
export function SetupWizard() {
  const router = useRouter();
  const { data: me, error: meError } = useMeQuery();

  const state = useQuery({
    queryKey: ["setup"],
    queryFn: () => unwrap(api.GET("/api/setup")),
    retry: false,
    enabled: !!me && !me.business?.setupCompleted,
  });

  // Oturum yoksa girişe, kurulum bittiyse panele.
  useEffect(() => {
    if (meError instanceof ApiError && meError.status === 401) router.replace("/giris");
    else if (me?.business?.setupCompleted) router.replace("/panel");
  }, [me, meError, router]);

  if (!state.data) {
    return (
      <p role="status" className="py-10 text-center text-sm text-neutral-600">
        {state.isError ? errorMessage(state.error) : "Yükleniyor…"}
      </p>
    );
  }

  return <WizardSteps initial={state.data} />;
}

type SetupState = components["schemas"]["SetupStateResponse"];

function WizardSteps({ initial }: { initial: SetupState }) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const saved = initial.business;
  // Kaldığı yerden devam: 1. adım kayıtlıysa 2. adımdan başlanır.
  const [step, setStep] = useState<0 | 1 | 2>(saved ? 1 : 0);
  const [hours, setHours] = useState<WorkingHour[]>(initial.workingHours.length ? initial.workingHours : defaultWeek());
  const [logoUrl, setLogoUrl] = useState<string | null>(saved?.logoUrl ?? null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const form = useForm<BusinessBasics>({
    resolver: zodResolver(businessBasicsSchema),
    defaultValues: saved
      ? { name: saved.name, sector: saved.sector, phone: formatPhone(saved.phone), city: saved.city ?? "", district: saved.district ?? "" }
      : { name: "", sector: undefined, phone: "", city: "", district: "" },
  });

  const saveBusiness = form.handleSubmit(async (values) => {
    setBusy(true);
    setError(null);
    try {
      const next = await unwrap(api.POST("/api/setup/business", { body: values }));
      queryClient.setQueryData(meQueryKey, next);
      setStep(1);
    } catch (e) {
      const fields = fieldErrors(e);
      for (const [name, message] of Object.entries(fields)) {
        if (name in values) form.setError(name as keyof BusinessBasics, { message });
      }
      if (!Object.keys(fields).length) setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  });

  const saveHours = async () => {
    const invalid = invalidDays(hours);
    if (invalid.length) return setError(`${invalid.join(", ")}: kapanış saati açılış saatinden sonra olmalı.`);
    setBusy(true);
    setError(null);
    try {
      setHours(await unwrap(api.PUT("/api/setup/working-hours", { body: { days: hours } })));
      setStep(2);
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  };

  const finish = async () => {
    setBusy(true);
    setError(null);
    try {
      const next = await unwrap(api.POST("/api/setup/complete"));
      queryClient.setQueryData(meQueryKey, next);
      toast.success("Kurulum tamam. Hoş geldin!");
      router.replace("/panel");
    } catch (e) {
      setError(errorMessage(e));
      setBusy(false);
    }
  };

  const next = step === 0 ? saveBusiness : step === 1 ? saveHours : finish;

  return (
    <div className="flex flex-col gap-6 pb-28">
      <div>
        <p className="text-sm font-semibold text-neutral-600">
          Adım {step + 1}/3 · {steps[step]}
        </p>
        <div className="mt-2 flex gap-1.5" aria-hidden>
          {steps.map((s, i) => (
            <span key={s} className={cn("h-1.5 flex-1 rounded-full", i <= step ? "bg-brand" : "bg-neutral-200")} />
          ))}
        </div>
      </div>

      {step === 0 && (
        <section className="flex flex-col gap-4">
          <div>
            <h1 className="text-2xl font-bold tracking-tight text-black">İşletmeni tanıyalım</h1>
            <p className="mt-1 text-sm text-neutral-600">Sektörün menüyü ve varsayılan ayarları belirler; sonra değiştirebilirsin.</p>
          </div>
          <form id="business-form" onSubmit={saveBusiness} noValidate>
            <BusinessFields
              control={form.control}
              register={form.register}
              errors={form.formState.errors}
              onCityChange={() => form.setValue("district", "")}
            />
          </form>
        </section>
      )}

      {step === 1 && (
        <section className="flex flex-col gap-4">
          <div>
            <h1 className="text-2xl font-bold tracking-tight text-black">Çalışma saatlerin</h1>
            <p className="mt-1 text-sm text-neutral-600">Online randevu ve takvim bu saatlere göre çalışır.</p>
          </div>
          <WorkingHoursEditor value={hours} onChange={setHours} />
        </section>
      )}

      {step === 2 && (
        <section className="flex flex-col gap-4">
          <div>
            <h1 className="text-2xl font-bold tracking-tight text-black">Logon var mı?</h1>
            <p className="mt-1 text-sm text-neutral-600">Tekliflerde ve randevu sayfanda görünür. İstersen bu adımı atlayabilirsin.</p>
          </div>
          <LogoUpload logoUrl={logoUrl} endpoint="/api/setup/logo" onChange={setLogoUrl} />
        </section>
      )}

      {error && <FormMessage>{error}</FormMessage>}

      <div className="fixed inset-x-0 bottom-0 z-20 border-t border-border bg-white px-4 pt-3 pb-[calc(0.75rem+env(safe-area-inset-bottom))]">
        <div className="mx-auto flex max-w-xl gap-3">
          {step > 0 && (
            <Button type="button" variant="outline" size="lg" disabled={busy} onClick={() => setStep((s) => (s - 1) as 0 | 1)}>
              Geri
            </Button>
          )}
          {step === 2 && !logoUrl && (
            <Button type="button" variant="ghost" size="lg" disabled={busy} onClick={finish}>
              Atla
            </Button>
          )}
          <Button
            type={step === 0 ? "submit" : "button"}
            form={step === 0 ? "business-form" : undefined}
            size="lg"
            className="flex-1"
            disabled={busy}
            onClick={step === 0 ? undefined : () => next()}
          >
            {busy ? "Kaydediliyor…" : step === 2 ? "Kurulumu bitir" : "Devam"}
          </Button>
        </div>
      </div>
    </div>
  );
}
