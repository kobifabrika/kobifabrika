"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";

import { useAfterAuth } from "@/components/auth/auth-helpers";
import { Field, FormMessage } from "@/components/forms/field";
import { PasswordInput } from "@/components/forms/password-input";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { api, errorMessage, unwrap } from "@/lib/api/client";

const schema = z.object({
  email: z.string().trim().min(1, "E-posta adresi girin."),
  password: z.string().min(1, "Şifre girin."),
  rememberMe: z.boolean(),
});

type Values = z.infer<typeof schema>;

const notices: Record<string, string> = {
  "sifre-yenilendi": "Şifren yenilendi. Yeni şifrenle giriş yapabilirsin.",
};

export function LoginForm() {
  const params = useSearchParams();
  const afterAuth = useAfterAuth();
  const [error, setError] = useState<string | null>(null);
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<Values>({ resolver: zodResolver(schema), defaultValues: { email: "", password: "", rememberMe: false } });

  const notice = notices[params.get("bilgi") ?? ""];

  const onSubmit = handleSubmit(async (values) => {
    setError(null);
    try {
      const me = await unwrap(api.POST("/api/auth/login", { body: values }));
      afterAuth(me, params.get("donus"));
    } catch (e) {
      setError(errorMessage(e));
    }
  });

  return (
    <form className="mt-8 flex flex-col gap-5" onSubmit={onSubmit} noValidate>
      {notice && <FormMessage tone="success">{notice}</FormMessage>}

      <Field label="E-posta" error={errors.email?.message}>
        <Input type="email" autoComplete="email" inputMode="email" placeholder="ornek@isletme.com" {...register("email")} />
      </Field>

      <Field label="Şifre" error={errors.password?.message}>
        <PasswordInput autoComplete="current-password" placeholder="Şifreniz" {...register("password")} />
      </Field>

      <div className="flex items-center justify-between gap-3 text-sm">
        <label className="flex min-h-11 items-center gap-2 text-muted-foreground">
          <input type="checkbox" className="size-5 accent-primary" {...register("rememberMe")} />
          Beni hatırla
        </label>
        <Link href="/sifremi-unuttum" className="flex min-h-11 items-center font-semibold text-primary hover:underline">
          Şifremi unuttum
        </Link>
      </div>

      {error && <FormMessage>{error}</FormMessage>}

      <Button type="submit" size="lg" className="w-full" disabled={isSubmitting}>
        {isSubmitting ? "Giriş yapılıyor…" : "Giriş yap"}
      </Button>
    </form>
  );
}
