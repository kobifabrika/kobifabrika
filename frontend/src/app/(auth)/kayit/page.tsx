import type { Metadata } from "next";
import { Suspense } from "react";

import { RedirectIfSignedIn } from "@/components/auth/auth-helpers";

import { RegisterView } from "./register-form";

export const metadata: Metadata = { title: "Üye ol", description: "KobiFabrika'ya ücretsiz üye olun; 10 dakikada kurun." };

export default function RegisterPage() {
  return (
    <>
      <Suspense>
        <RedirectIfSignedIn />
      </Suspense>
      <RegisterView />
    </>
  );
}
