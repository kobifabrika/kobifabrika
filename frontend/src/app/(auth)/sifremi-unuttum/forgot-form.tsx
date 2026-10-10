"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import Link from "next/link";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";

import { Field, FormMessage } from "@/components/forms/field";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { api, errorMessage, unwrap } from "@/lib/api/client";
import { emailSchema } from "@/lib/validation/rules";

const schema = z.object({ email: emailSchema });

export function ForgotForm() {
  const [sent, setSent] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<z.infer<typeof schema>>({ resolver: zodResolver(schema), defaultValues: { email: "" } });

  const onSubmit = handleSubmit(async (values) => {
    setError(null);
    try {
      await unwrap(api.POST("/api/auth/forgot-password", { body: values }));
      setSent(true);
    } catch (e) {
      setError(errorMessage(e));
    }
  });

  return (
    <>
      {sent ? (
        <div className="mt-8 flex flex-col gap-5">
          <FormMessage tone="success">
            Bu adrese kayıtlı bir hesap varsa sıfırlama bağlantısı gönderdik. Gelen kutunuzu (ve gereksiz klasörünü)
            kontrol edin. Bağlantı 2 saat geçerlidir.
          </FormMessage>
          <Button asChild size="lg" variant="outline" className="w-full">
            <Link href="/giris">Giriş ekranına dön</Link>
          </Button>
        </div>
      ) : (
        <form className="mt-8 flex flex-col gap-5" onSubmit={onSubmit} noValidate>
          <Field label="E-posta" error={errors.email?.message}>
            <Input type="email" autoComplete="email" inputMode="email" placeholder="ornek@isletme.com" {...register("email")} />
          </Field>
          {error && <FormMessage>{error}</FormMessage>}
          <Button type="submit" size="lg" className="w-full" disabled={isSubmitting}>
            {isSubmitting ? "Gönderiliyor…" : "Sıfırlama bağlantısı gönder"}
          </Button>
          <Link href="/giris" className="inline-flex min-h-11 items-center justify-center text-sm font-semibold text-black underline decoration-brand decoration-2 underline-offset-4 hover:decoration-black">
            Giriş ekranına dön
          </Link>
        </form>
      )}
    </>
  );
}
