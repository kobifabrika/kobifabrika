"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useState } from "react";
import { useForm, useWatch } from "react-hook-form";
import { z } from "zod";

import { PhoneInput, useAfterAuth } from "@/components/auth/auth-helpers";
import { Field, FormMessage } from "@/components/forms/field";
import { KvkkConsent } from "@/components/forms/kvkk-consent";
import { PasswordHints, PasswordInput } from "@/components/forms/password-input";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { api, errorMessage, fieldErrors, unwrap } from "@/lib/api/client";
import { emailSchema, fullNameSchema, passwordSchema, phoneSchema } from "@/lib/validation/rules";

const schema = z.object({
  fullName: fullNameSchema,
  email: emailSchema,
  phone: phoneSchema,
  password: passwordSchema,
  kvkkAccepted: z.boolean().refine((v) => v, "Devam etmek için KVKK aydınlatma metnini onaylayın."),
});

type Values = z.infer<typeof schema>;

/** Kayıt: işletme adı burada sorulmaz, kurulum sihirbazında sorulur. Kayıt olan kişi işletmenin sahibi olur. */
export function RegisterForm() {
  const afterAuth = useAfterAuth();
  const [error, setError] = useState<string | null>(null);
  const {
    register,
    handleSubmit,
    control,
    setError: setFieldError,
    formState: { errors, isSubmitting },
  } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { fullName: "", email: "", phone: "", password: "", kvkkAccepted: false },
  });
  const password = useWatch({ control, name: "password" });

  const onSubmit = handleSubmit(async (values) => {
    setError(null);
    try {
      afterAuth(await unwrap(api.POST("/api/auth/register", { body: values })));
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

      <KvkkConsent error={errors.kvkkAccepted?.message} {...register("kvkkAccepted")} />

      {error && <FormMessage>{error}</FormMessage>}

      <Button type="submit" size="lg" className="w-full" disabled={isSubmitting}>
        {isSubmitting ? "Hesap oluşturuluyor…" : "Hesap oluştur"}
      </Button>
    </form>
  );
}
