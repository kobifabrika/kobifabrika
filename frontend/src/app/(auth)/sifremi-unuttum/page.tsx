import type { Metadata } from "next";

import { ForgotForm } from "./forgot-form";

export const metadata: Metadata = { title: "Şifremi unuttum" };

export default function ForgotPasswordPage() {
  return (
    <>
      <h1 className="text-3xl font-bold tracking-tight text-foreground">Şifremi unuttum</h1>
      <p className="mt-2 text-sm text-muted-foreground">E-posta adresinizi yazın; şifre sıfırlama bağlantısı gönderelim.</p>
      <ForgotForm />
    </>
  );
}
