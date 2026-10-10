"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useParams, useRouter } from "next/navigation";
import { useState } from "react";
import { useForm, useWatch } from "react-hook-form";
import { toast } from "sonner";
import { z } from "zod";

import { PhoneInput } from "@/components/auth/auth-helpers";
import { Field, FormMessage } from "@/components/forms/field";
import { KvkkConsent } from "@/components/forms/kvkk-consent";
import { PasswordHints, PasswordInput } from "@/components/forms/password-input";
import { meQueryKey, useMeQuery } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { api, errorMessage, fieldErrors, unwrap } from "@/lib/api/client";
import { roleDescriptions, roleNames, type Me, type Role } from "@/lib/api/types";
import { fullNameSchema, passwordSchema, phoneSchema } from "@/lib/validation/rules";

/**
 * /davet/[token]: (a) oturum açık ve e-posta eşleşiyor → kabul; (b) hesap yok → kayıt formu (e-posta kilitli);
 * (c) hesap var, oturum yok → giriş formu, ardından otomatik kabul.
 */
export function InvitationView() {
  const { token } = useParams<{ token: string }>();
  const router = useRouter();
  const queryClient = useQueryClient();
  const { data: me, isPending: mePending } = useMeQuery();

  const preview = useQuery({
    queryKey: ["invitation", token],
    queryFn: () => unwrap(api.GET("/api/invitations/{token}", { params: { path: { token } } })),
    retry: false,
  });

  const joined = (result: Me) => {
    queryClient.setQueryData(meQueryKey, result);
    toast.success(`${result.business?.name ?? "Ekip"} ekibine katıldın.`);
    router.replace("/panel");
  };

  if (preview.isPending || mePending) {
    return (
      <p role="status" className="mt-8 text-sm text-neutral-600">
        Davet yükleniyor…
      </p>
    );
  }

  if (preview.isError) {
    return (
      <div className="mt-8">
        <FormMessage>{errorMessage(preview.error)}</FormMessage>
      </div>
    );
  }

  const invitation = preview.data;
  const role = invitation.role as Role;

  return (
    <div className="mt-6 flex flex-col gap-6">
      <div className="rounded-lg border border-border bg-neutral-50 p-4">
        <p className="text-sm text-neutral-600">
          <span className="font-semibold text-black">{invitation.invitedBy}</span> seni davet etti:
        </p>
        <p className="mt-1 text-xl font-bold text-black">{invitation.businessName}</p>
        <p className="mt-2 text-sm text-black">
          Rol: <span className="font-semibold">{roleNames[role]}</span> — {roleDescriptions[role]}
        </p>
      </div>

      {invitation.status !== "Pending" ? (
        <FormMessage>
          {invitation.status === "Accepted"
            ? "Bu davet zaten kullanılmış."
            : invitation.status === "Revoked"
              ? "Bu davet iptal edilmiş."
              : "Bu davetin süresi dolmuş. İşletmeden yeni bir davet isteyin."}
        </FormMessage>
      ) : me ? (
        <AcceptAsCurrentUser token={token} me={me} invitedEmail={invitation.email} onJoined={joined} />
      ) : invitation.hasAccount ? (
        <LoginAndAccept token={token} email={invitation.email} onJoined={joined} />
      ) : (
        <RegisterAndAccept token={token} email={invitation.email} onJoined={joined} />
      )}
    </div>
  );
}

function AcceptAsCurrentUser({
  token,
  me,
  invitedEmail,
  onJoined,
}: {
  token: string;
  me: Me;
  invitedEmail: string;
  onJoined: (me: Me) => void;
}) {
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  if (me.user.email.toLowerCase() !== invitedEmail.toLowerCase()) {
    return (
      <div className="flex flex-col gap-4">
        <FormMessage tone="info">
          Şu an <strong>{me.user.email}</strong> hesabıyla giriş yaptınız. Bu davet <strong>{invitedEmail}</strong> adresine
          gönderildi.
        </FormMessage>
        <Button
          size="lg"
          variant="outline"
          className="w-full"
          onClick={async () => {
            // Sayfada kalınır: oturum kapanınca giriş/kayıt formu görünür.
            await api.POST("/api/auth/logout").catch(() => undefined);
            await queryClient.resetQueries({ queryKey: meQueryKey });
          }}
        >
          Çıkış yap ve {invitedEmail} ile devam et
        </Button>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-4">
      {error && <FormMessage>{error}</FormMessage>}
      <Button
        size="lg"
        className="w-full"
        disabled={busy}
        onClick={async () => {
          setBusy(true);
          setError(null);
          try {
            onJoined(await unwrap(api.POST("/api/invitations/{token}/accept", { params: { path: { token } } })));
          } catch (e) {
            setError(errorMessage(e));
            setBusy(false);
          }
        }}
      >
        {busy ? "Katılıyor…" : "Daveti kabul et"}
      </Button>
    </div>
  );
}

function LoginAndAccept({ token, email, onJoined }: { token: string; email: string; onJoined: (me: Me) => void }) {
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  return (
    <form
      className="flex flex-col gap-5"
      noValidate
      onSubmit={async (e) => {
        e.preventDefault();
        if (!password) return setError("Şifre girin.");
        setBusy(true);
        setError(null);
        try {
          await unwrap(api.POST("/api/auth/login", { body: { email, password, rememberMe: false } }));
          onJoined(await unwrap(api.POST("/api/invitations/{token}/accept", { params: { path: { token } } })));
        } catch (err) {
          setError(errorMessage(err));
          setBusy(false);
        }
      }}
    >
      <p className="text-sm text-neutral-600">Bu e-postayla bir hesabın var. Daveti kabul etmek için giriş yap.</p>
      <Field label="E-posta">
        <Input type="email" value={email} readOnly className="bg-neutral-100" />
      </Field>
      <Field label="Şifre">
        <PasswordInput autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} />
      </Field>
      {error && <FormMessage>{error}</FormMessage>}
      <Button type="submit" size="lg" className="w-full" disabled={busy}>
        {busy ? "Giriş yapılıyor…" : "Giriş yap ve katıl"}
      </Button>
    </form>
  );
}

const registerSchema = z.object({
  fullName: fullNameSchema,
  phone: phoneSchema,
  password: passwordSchema,
  kvkkAccepted: z.boolean().refine((v) => v, "Devam etmek için KVKK aydınlatma metnini onaylayın."),
});

type RegisterValues = z.infer<typeof registerSchema>;

function RegisterAndAccept({ token, email, onJoined }: { token: string; email: string; onJoined: (me: Me) => void }) {
  const [error, setError] = useState<string | null>(null);
  const {
    register,
    handleSubmit,
    control,
    setError: setFieldError,
    formState: { errors, isSubmitting },
  } = useForm<RegisterValues>({
    resolver: zodResolver(registerSchema),
    defaultValues: { fullName: "", phone: "", password: "", kvkkAccepted: false },
  });
  const password = useWatch({ control, name: "password" });

  const onSubmit = handleSubmit(async (values) => {
    setError(null);
    try {
      onJoined(await unwrap(api.POST("/api/invitations/{token}/register", { params: { path: { token } }, body: values })));
    } catch (e) {
      const fields = fieldErrors(e);
      for (const [name, message] of Object.entries(fields)) {
        if (name in values) setFieldError(name as keyof RegisterValues, { message });
      }
      if (Object.keys(fields).length === 0) setError(errorMessage(e));
    }
  });

  return (
    <form className="flex flex-col gap-5" onSubmit={onSubmit} noValidate>
      <p className="text-sm text-neutral-600">Hesabını oluştur; ekibe hemen katılırsın.</p>
      <Field label="E-posta" hint="Davet bu adrese gönderildi.">
        <Input type="email" value={email} readOnly className="bg-neutral-100" />
      </Field>
      <Field label="Ad soyad" error={errors.fullName?.message}>
        <Input autoComplete="name" {...register("fullName")} />
      </Field>
      <Field label="Cep telefonu" error={errors.phone?.message}>
        <PhoneInput {...register("phone")} />
      </Field>
      <Field label="Şifre" error={errors.password?.message} hint={<PasswordHints value={password} />}>
        <PasswordInput autoComplete="new-password" {...register("password")} />
      </Field>
      <KvkkConsent error={errors.kvkkAccepted?.message} {...register("kvkkAccepted")} />
      {error && <FormMessage>{error}</FormMessage>}
      <Button type="submit" size="lg" className="w-full" disabled={isSubmitting}>
        {isSubmitting ? "Hesap oluşturuluyor…" : "Hesap oluştur ve katıl"}
      </Button>
    </form>
  );
}
