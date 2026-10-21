import type { Metadata } from "next";
import { Suspense } from "react";

import { ResetForm } from "./reset-form";

export const metadata: Metadata = { title: "Yeni şifre belirle" };

export default function ResetPasswordPage() {
  return (
    <>
      <h1 className="text-3xl font-bold tracking-tight text-black">Yeni şifre belirle</h1>
      <p className="mt-2 text-sm text-neutral-600">Şifreniz değişince diğer cihazlardaki oturumlarınız kapanır.</p>
      <Suspense>
        <ResetForm />
      </Suspense>
    </>
  );
}
