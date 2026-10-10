import type { Metadata } from "next";
import Link from "next/link";
import { Suspense } from "react";

import { RedirectIfSignedIn } from "@/components/auth/auth-helpers";

import { LoginForm } from "./login-form";

export const metadata: Metadata = { title: "Giriş yap" };

export default function LoginPage() {
  return (
    <>
      <h1 className="text-3xl font-bold tracking-tight text-black">Hoş geldiniz</h1>
      <p className="mt-2 text-sm text-neutral-600">KobiFabrika hesabınıza giriş yapın.</p>

      <Suspense>
        <RedirectIfSignedIn />
        <LoginForm />
      </Suspense>

      <p className="mt-6 text-center text-sm text-neutral-600">
        KobiFabrika hesabınız yok mu?{" "}
        <Link href="/kayit" className="inline-flex min-h-11 items-center font-semibold text-black underline decoration-brand decoration-2 underline-offset-4 hover:decoration-black">
          Üye ol
        </Link>
      </p>
    </>
  );
}
