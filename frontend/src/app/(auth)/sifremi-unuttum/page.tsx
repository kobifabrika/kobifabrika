import type { Metadata } from "next";

import { ForgotForm } from "./forgot-form";

export const metadata: Metadata = { title: "Şifremi unuttum" };

export default function ForgotPasswordPage() {
  return (
    <>
      <h1 className="text-3xl font-bold tracking-tight text-black">Şifremi unuttum</h1>
      <p className="mt-2 text-sm text-neutral-600">E-posta adresinizi yazın; şifre sıfırlama bağlantısı gönderelim.</p>
      <ForgotForm />
    </>
  );
}
