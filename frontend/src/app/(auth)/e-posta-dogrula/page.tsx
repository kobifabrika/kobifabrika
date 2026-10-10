import type { Metadata } from "next";
import { Suspense } from "react";

import { VerifyEmail } from "./verify-email";

export const metadata: Metadata = { title: "E-posta doğrulama" };

export default function VerifyEmailPage() {
  return (
    <>
      <h1 className="text-3xl font-bold tracking-tight text-foreground">E-posta doğrulama</h1>
      <Suspense>
        <VerifyEmail />
      </Suspense>
    </>
  );
}
