"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useState } from "react";
import { useForm, useWatch } from "react-hook-form";
import { z } from "zod";

import { Field, FormMessage } from "@/components/forms/field";
import { PasswordHints, PasswordInput } from "@/components/forms/password-input";
import { Button } from "@/components/ui/button";
import { api, errorMessage, fieldErrors, unwrap } from "@/lib/api/client";
import { passwordSchema } from "@/lib/validation/rules";

const schema = z
  .object({ newPassword: passwordSchema, repeat: z.string() })
  .refine((v) => v.newPassword === v.repeat, { path: ["repeat"], message: "Şifreler aynı değil." });

export function ResetForm() {
  const params = useSearchParams();
  const router = useRouter();
  const email = params.get("e") ?? "";
  const token = params.get("t") ?? "";
  const [error, setError] = useState<string | null>(null);
  const {
    register,
    handleSubmit,
    control,
    formState: { errors, isSubmitting },
  } = useForm<z.infer<typeof schema>>({ resolver: zodResolver(schema), defaultValues: { newPassword: "", repeat: "" } });
  const password = useWatch({ control, name: "newPassword" });

  if (!email || !token) {
    return (
      <div className="mt-8 flex flex-col gap-5">
        <FormMessage>Bağlantı eksik veya bozuk. E-postadaki bağlantıyı tekrar açın ya da yeni bağlantı isteyin.</FormMessage>
        <NewLinkButton />
      </div>
    );
  }

  const onSubmit = handleSubmit(async ({ newPassword }) => {
    setError(null);
    try {
      await unwrap(api.POST("/api/auth/reset-password", { body: { email, token, newPassword } }));
      router.replace("/giris?bilgi=sifre-yenilendi");
    } catch (e) {
      setError(fieldErrors(e).token ?? errorMessage(e));
    }
  });

  return (
    <form className="mt-8 flex flex-col gap-5" onSubmit={onSubmit} noValidate>
      <p className="text-sm text-muted-foreground">
        Hesap: <span className="font-semibold text-foreground">{email}</span>
      </p>
      <Field label="Yeni şifre" error={errors.newPassword?.message} hint={<PasswordHints value={password} />}>
        <PasswordInput autoComplete="new-password" {...register("newPassword")} />
      </Field>
      <Field label="Yeni şifre (tekrar)" error={errors.repeat?.message}>
        <PasswordInput autoComplete="new-password" {...register("repeat")} />
      </Field>
      {error && (
        <>
          <FormMessage>{error}</FormMessage>
          <NewLinkButton />
        </>
      )}
      <Button type="submit" size="lg" className="w-full" disabled={isSubmitting}>
        {isSubmitting ? "Kaydediliyor…" : "Şifremi yenile"}
      </Button>
    </form>
  );
}

function NewLinkButton() {
  return (
    <Link href="/sifremi-unuttum" className="inline-flex min-h-11 items-center justify-center text-sm font-semibold text-primary hover:underline">
      Yeni sıfırlama bağlantısı iste
    </Link>
  );
}
