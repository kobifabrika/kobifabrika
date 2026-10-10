import type { Metadata } from "next";
import Link from "next/link";

export const metadata: Metadata = { title: "Şifremi unuttum" };

export default function ForgotPasswordPage() {
  return (
    <>
      <h1 className="text-3xl font-bold tracking-tight text-brand-900">Şifremi unuttum</h1>
      <p className="mt-2 text-sm text-neutral-600">Şifre sıfırlama Sprint 1&apos;de hazır olacak.</p>
      <Link href="/giris" className="mt-6 inline-flex min-h-11 items-center font-semibold text-brand-500 hover:underline">
        Giriş ekranına dön
      </Link>
    </>
  );
}
