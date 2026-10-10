import type { Metadata } from "next";
import Link from "next/link";
import { Suspense } from "react";

import { RedirectIfSignedIn } from "@/components/auth/auth-helpers";

import { LoginForm } from "./login-form";

export const metadata: Metadata = { title: "Giriş yap" };

export default function LoginPage() {
  return (
    <>
      <h1 className="text-3xl font-bold tracking-tight text-foreground">Hoş geldiniz</h1>
      <p className="mt-2 text-sm text-muted-foreground">KobiFabrika hesabınıza giriş yapın.</p>

      <Suspense>
        <RedirectIfSignedIn />
        <LoginForm />
      </Suspense>

      <p className="mt-6 text-center text-sm text-muted-foreground">
        KobiFabrika hesabınız yok mu?{" "}
        <Link href="/kayit" className="inline-flex min-h-11 items-center font-semibold text-primary hover:underline">
          Üye ol
        </Link>
      </p>
    </>
  );
}
