import type { Metadata } from "next";
import { Suspense } from "react";

import { ExpenseFormPage } from "./expense-form-page";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Gelir / gider ekle" };

export default function NewExpensePage() {
  return (
    <Suspense>
      <ExpenseFormPage />
    </Suspense>
  );
}
