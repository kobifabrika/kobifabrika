import type { Metadata } from "next";
import Link from "next/link";
import { Suspense } from "react";

import { RedirectIfSignedIn } from "@/components/auth/auth-helpers";

import { RegisterForm } from "./register-form";

export const metadata: Metadata = { title: "Üye ol" };

export default function RegisterPage() {
  return (
    <>
      <h1 className="text-3xl font-bold tracking-tight text-black">Üye ol</h1>
      <p className="mt-2 text-sm text-neutral-600">Hesabınızı oluşturun; işletme bilgilerini sonraki adımda soracağız.</p>

      <Suspense>
        <RedirectIfSignedIn />
      </Suspense>
      <RegisterForm />

      <p className="mt-6 text-center text-sm text-neutral-600">
        Zaten hesabınız var mı?{" "}
        <Link href="/giris" className="inline-flex min-h-11 items-center font-semibold text-black underline decoration-brand decoration-2 underline-offset-4 hover:decoration-black">
          Giriş yap
        </Link>
      </p>
    </>
  );
}
