"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation } from "@tanstack/react-query";
import { MailCheck } from "lucide-react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { Suspense, useState } from "react";
import { toast } from "sonner";
import { useForm, useWatch } from "react-hook-form";
import { z } from "zod";

import { PhoneInput, useAfterAuth } from "@/components/auth/auth-helpers";
import { Field, FormMessage } from "@/components/forms/field";
import { TermsConsent } from "@/components/forms/terms-consent";
import { PasswordHints, PasswordInput } from "@/components/forms/password-input";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { api, errorMessage, fieldErrors, unwrap } from "@/lib/api/client";
import type { Me } from "@/lib/api/types";
import { plans, type Period } from "@/content/plans";
import { emailSchema, fullNameSchema, passwordSchema, phoneSchema } from "@/lib/validation/rules";

const schema = z.object({
  fullName: fullNameSchema,
  email: emailSchema,
  phone: phoneSchema,
  password: passwordSchema,
  termsAccepted: z.boolean().refine((v) => v, "Devam etmek için Kullanım koşullarını ve Veri işleme sözleşmesini kabul edin."),
});

type Values = z.infer<typeof schema>;

/** ?paket=kalfa&donem=yillik (fiyat sayfasından): geçerliyse paket ve dönem; dönem yoksa aylık. */
export function requestedPlan(params: URLSearchParams): { plan: (typeof plans)[number]; period: Period } | null {
  const plan = plans.find((p) => p.id === params.get("paket"));
  if (!plan) return null;
  return { plan, period: params.get("donem") === "yillik" ? "yillik" : "aylik" };
}

/**
 * Kayıt ekranı: işletme adı burada sorulmaz, kurulum sihirbazında sorulur; kayıt olan kişi işletmenin sahibi olur. Fiyat
 * sayfasından gelindiyse seçilen paket gösterilir ve kayda yazılır (ödeme yok, deneme bitince Sahip seçer). Kayıttan sonra
 * "E-postanızı kontrol edin" ekranı: doğrulama bağlantısı, tekrar gönder, gelmezse ne yapmalı ve kuruluma devam.
 */
export function RegisterView() {
  const [registered, setRegistered] = useState<Me | null>(null);
  if (registered) return <CheckEmail me={registered} />;
  return (
    <>
      <h1 className="text-3xl font-bold tracking-tight text-black">Üye ol</h1>
      <p className="mt-2 text-sm text-neutral-600">Hesabınızı oluşturun; işletme bilgilerini sonraki adımda soracağız.</p>
      <RegisterForm onRegistered={setRegistered} />
      <p className="mt-6 text-center text-sm text-neutral-600">
        Zaten hesabınız var mı?{" "}
        <Link
          href="/giris"
          className="inline-flex min-h-11 items-center font-semibold text-black underline decoration-brand decoration-2 underline-offset-4 hover:decoration-black"
        >
          Giriş yap
        </Link>
      </p>
    </>
  );
}

function CheckEmail({ me }: { me: Me }) {
  const afterAuth = useAfterAuth();
  const resend = useMutation({
    mutationFn: () => unwrap(api.POST("/api/auth/resend-confirmation")),
    onSuccess: () => toast.success("Doğrulama e-postası tekrar gönderildi."),
    onError: (e) => toast.error(errorMessage(e)),
  });
  return (
    <section aria-labelledby="eposta-kontrol" className="flex flex-col gap-5">
      <MailCheck aria-hidden className="size-10 text-link" />
      <h1 id="eposta-kontrol" className="text-3xl font-bold tracking-tight text-black">
        E-postanızı kontrol edin
      </h1>
      <p className="text-base text-neutral-700">
        <span className="font-semibold break-all text-black">{me.user.email}</span> adresine bir doğrulama bağlantısı gönderdik.
        Bağlantıya dokununca hesabınız doğrulanır.
      </p>
      <ol className="flex flex-col gap-2 border-l-2 border-brand bg-neutral-50 px-4 py-3 text-sm text-neutral-700">
        <li>1. E-posta kutunuzu açın; &quot;E-posta adresinizi doğrulayın&quot; konulu e-postayı bulun.</li>
        <li>2. Birkaç dakikada gelmezse İstenmeyen (Spam) ve Promosyonlar klasörüne bakın.</li>
        <li>3. Hâlâ yoksa aşağıdan tekrar gönderin.</li>
      </ol>
      <p className="text-sm text-neutral-600">Beklemenize gerek yok: işletmenizi şimdi kurabilir, e-postayı sonra doğrulayabilirsiniz.</p>
      <Button size="lg" className="w-full" onClick={() => afterAuth(me)}>
        İşletmemi kurmaya başla
      </Button>
      <Button variant="outline" className="w-full" disabled={resend.isPending || resend.isSuccess} onClick={() => resend.mutate()}>
        {resend.isSuccess ? "Tekrar gönderildi" : "E-postayı tekrar gönder"}
      </Button>
      <p className="text-sm text-neutral-600">
        E-posta adresinizi yanlış mı yazdınız?{" "}
        <a href="mailto:destek@kobifabrika.com" className="font-semibold text-link underline">
          destek@kobifabrika.com
        </a>{" "}
        adresine yazabilirsiniz.
      </p>
    </section>
  );
}

/** Fiyat sayfasından seçilen paket (sadece bu bant adres parametresini okur: form sunucuda çizilir, LCP erken). */
function ChosenPlan() {
  const chosen = requestedPlan(useSearchParams());
  if (!chosen) return null;
  return (
    <p className="border-l-2 border-brand bg-neutral-50 px-4 py-3 text-sm text-black">
      Seçtiğiniz paket: <span className="font-semibold">{chosen.plan.name}</span>
      {chosen.plan.monthlyPrice > 0 && ` (${chosen.period === "yillik" ? "yıllık" : "aylık"}) · 14 gün ücretsiz deneyin`}
    </p>
  );
}

function RegisterForm({ onRegistered }: { onRegistered: (me: Me) => void }) {
  const [error, setError] = useState<string | null>(null);
  const {
    register,
    handleSubmit,
    control,
    setError: setFieldError,
    formState: { errors, isSubmitting },
  } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { fullName: "", email: "", phone: "", password: "", termsAccepted: false },
  });
  const password = useWatch({ control, name: "password" });

  const onSubmit = handleSubmit(async (values) => {
    setError(null);
    const chosen = requestedPlan(new URLSearchParams(window.location.search));
    try {
      onRegistered(
        await unwrap(
          api.POST("/api/auth/register", {
            body: { ...values, plan: chosen?.plan.id ?? null, period: chosen && chosen.plan.monthlyPrice > 0 ? chosen.period : null },
          }),
        ),
      );
    } catch (e) {
      const fields = fieldErrors(e);
      for (const [name, message] of Object.entries(fields)) {
        if (name in values) setFieldError(name as keyof Values, { message });
      }
      if (Object.keys(fields).length === 0) setError(errorMessage(e));
    }
  });

  return (
    <form className="mt-8 flex flex-col gap-5" onSubmit={onSubmit} noValidate>
      <Suspense>
        <ChosenPlan />
      </Suspense>
      <Field label="Ad soyad" error={errors.fullName?.message}>
        <Input autoComplete="name" placeholder="Adınız ve soyadınız" {...register("fullName")} />
      </Field>

      <Field label="E-posta" error={errors.email?.message}>
        <Input type="email" autoComplete="email" inputMode="email" placeholder="ornek@isletme.com" {...register("email")} />
      </Field>

      <Field label="Cep telefonu" error={errors.phone?.message}>
        <PhoneInput {...register("phone")} />
      </Field>

      <Field label="Şifre" error={errors.password?.message} hint={<PasswordHints value={password} />}>
        <PasswordInput autoComplete="new-password" placeholder="En az 8 karakter" {...register("password")} />
      </Field>

      <TermsConsent error={errors.termsAccepted?.message} {...register("termsAccepted")} />

      {error && <FormMessage>{error}</FormMessage>}

      <Button type="submit" size="lg" className="w-full" disabled={isSubmitting}>
        {isSubmitting ? "Hesap oluşturuluyor…" : "Hesap oluştur"}
      </Button>
    </form>
  );
}
