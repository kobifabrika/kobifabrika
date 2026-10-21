"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useQueryClient } from "@tanstack/react-query";
import { CircleCheck, MailWarning } from "lucide-react";
import { useState } from "react";
import { useForm, useWatch } from "react-hook-form";
import { toast } from "sonner";
import { z } from "zod";

import { PhoneInput } from "@/components/auth/auth-helpers";
import { Field, FormMessage } from "@/components/forms/field";
import { PasswordHints, PasswordInput } from "@/components/forms/password-input";
import { meQueryKey, useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { api, errorMessage, fieldErrors, unwrap } from "@/lib/api/client";
import { roleNames } from "@/lib/api/types";
import { formatPhone, fullNameSchema, passwordSchema, phoneSchema } from "@/lib/validation/rules";

export function AccountSettings() {
  return (
    <div className="mx-auto flex max-w-2xl flex-col gap-6">
      <h1 className="text-2xl font-bold tracking-tight text-black">Hesabım</h1>
      <ProfileCard />
      <PasswordCard />
    </div>
  );
}

const profileSchema = z.object({ fullName: fullNameSchema, phone: phoneSchema });

function ProfileCard() {
  const { me, business, role } = useSession();
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | null>(null);
  const form = useForm<z.infer<typeof profileSchema>>({
    resolver: zodResolver(profileSchema),
    defaultValues: { fullName: me.user.fullName, phone: formatPhone(me.user.phone) },
  });

  const onSubmit = form.handleSubmit(async (values) => {
    setError(null);
    try {
      await unwrap(api.PUT("/api/auth/profile", { body: values }));
      await queryClient.invalidateQueries({ queryKey: meQueryKey });
      form.reset(values);
      toast.success("Bilgileriniz kaydedildi.");
    } catch (e) {
      const fields = fieldErrors(e);
      for (const [name, message] of Object.entries(fields)) {
        if (name === "fullName" || name === "phone") form.setError(name, { message });
      }
      if (!Object.keys(fields).length) setError(errorMessage(e));
    }
  });

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-black">Profil</CardTitle>
        <CardDescription>
          {business.name} · {roleNames[role]}
        </CardDescription>
      </CardHeader>
      <CardContent>
        <form className="flex flex-col gap-5" onSubmit={onSubmit} noValidate>
          <Field
            label="E-posta"
            hint={
              me.user.emailConfirmed ? (
                <span className="flex items-center gap-1 text-black">
                  <CircleCheck aria-hidden className="size-4" /> Doğrulandı
                </span>
              ) : (
                <span className="flex items-center gap-1 text-neutral-600">
                  <MailWarning aria-hidden className="size-4" /> Doğrulanmadı — sayfanın üstünden yeni bağlantı isteyebilirsiniz.
                </span>
              )
            }
          >
            <Input type="email" value={me.user.email} readOnly className="bg-neutral-100" />
          </Field>
          <Field label="Ad soyad" error={form.formState.errors.fullName?.message}>
            <Input autoComplete="name" {...form.register("fullName")} />
          </Field>
          <Field label="Cep telefonu" error={form.formState.errors.phone?.message}>
            <PhoneInput {...form.register("phone")} />
          </Field>
          {error && <FormMessage>{error}</FormMessage>}
          <Button type="submit" size="lg" className="self-start" disabled={form.formState.isSubmitting || !form.formState.isDirty}>
            {form.formState.isSubmitting ? "Kaydediliyor…" : "Kaydet"}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}

const passwordFormSchema = z
  .object({ currentPassword: z.string().min(1, "Mevcut şifreyi girin."), newPassword: passwordSchema, repeat: z.string() })
  .refine((v) => v.newPassword === v.repeat, { path: ["repeat"], message: "Şifreler aynı değil." });

type PasswordValues = z.infer<typeof passwordFormSchema>;

function PasswordCard() {
  const [error, setError] = useState<string | null>(null);
  const form = useForm<PasswordValues>({
    resolver: zodResolver(passwordFormSchema),
    defaultValues: { currentPassword: "", newPassword: "", repeat: "" },
  });
  const password = useWatch({ control: form.control, name: "newPassword" });

  const onSubmit = form.handleSubmit(async ({ currentPassword, newPassword }) => {
    setError(null);
    try {
      await unwrap(api.POST("/api/auth/change-password", { body: { currentPassword, newPassword } }));
      form.reset();
      toast.success("Şifreniz değiştirildi. Diğer cihazlardaki oturumlarınız kapanacak.");
    } catch (e) {
      const fields = fieldErrors(e);
      if (fields.currentPassword) form.setError("currentPassword", { message: fields.currentPassword });
      else if (fields.password) form.setError("newPassword", { message: fields.password });
      else setError(errorMessage(e));
    }
  });

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-black">Şifre değiştir</CardTitle>
      </CardHeader>
      <CardContent>
        <form className="flex flex-col gap-5" onSubmit={onSubmit} noValidate>
          <Field label="Mevcut şifre" error={form.formState.errors.currentPassword?.message}>
            <PasswordInput autoComplete="current-password" {...form.register("currentPassword")} />
          </Field>
          <Field label="Yeni şifre" error={form.formState.errors.newPassword?.message} hint={<PasswordHints value={password} />}>
            <PasswordInput autoComplete="new-password" {...form.register("newPassword")} />
          </Field>
          <Field label="Yeni şifre (tekrar)" error={form.formState.errors.repeat?.message}>
            <PasswordInput autoComplete="new-password" {...form.register("repeat")} />
          </Field>
          {error && <FormMessage>{error}</FormMessage>}
          <Button type="submit" size="lg" className="self-start" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting ? "Kaydediliyor…" : "Şifreyi değiştir"}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}
