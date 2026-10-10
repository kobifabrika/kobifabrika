import type { Metadata } from "next";
import Link from "next/link";
import { Suspense } from "react";

import { RedirectIfSignedIn } from "@/components/auth/auth-helpers";

import { RegisterForm } from "./register-form";

export const metadata: Metadata = { title: "Üye ol" };

export default function RegisterPage() {
  return (
    <>
      <h1 className="text-3xl font-bold tracking-tight text-foreground">Üye ol</h1>
      <p className="mt-2 text-sm text-muted-foreground">Hesabınızı oluşturun; işletme bilgilerini sonraki adımda soracağız.</p>

      <Suspense>
        <RedirectIfSignedIn />
      </Suspense>
      <RegisterForm />

      <p className="mt-6 text-center text-sm text-muted-foreground">
        Zaten hesabınız var mı?{" "}
        <Link href="/giris" className="inline-flex min-h-11 items-center font-semibold text-primary hover:underline">
          Giriş yap
        </Link>
      </p>
    </>
  );
}
