"use client";

import { ArrowLeft } from "lucide-react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";

import { ExpenseForm } from "@/components/finance/expenses";
import { RequirePermission } from "@/components/panel/settings-nav";

/** /panel/cari-kasa/gelir-gider/yeni?tur=gider|gelir — telefonda (+) → "Gider ekle". */
export function ExpenseFormPage() {
  const params = useSearchParams();
  const kind = params.get("tur") === "gelir" ? "Income" : "Expense";
  const staffId = kind === "Expense" ? params.get("personel") : null;
  return (
    <div className="mx-auto flex w-full max-w-xl flex-col gap-4">
      <Link
        href="/panel/cari-kasa?sekme=gelir-gider"
        className="flex min-h-11 items-center gap-1 self-start text-sm font-semibold text-black underline decoration-brand underline-offset-4"
      >
        <ArrowLeft aria-hidden className="size-4" />
        Gelir-gider
      </Link>
      <h1 className="text-2xl font-bold tracking-tight text-black">{kind === "Income" ? "Gelir ekle" : "Gider ekle"}</h1>
      <RequirePermission permission="manageFinance">
        <ExpenseForm key={kind} kind={kind} staffId={staffId} />
      </RequirePermission>
    </div>
  );
}
