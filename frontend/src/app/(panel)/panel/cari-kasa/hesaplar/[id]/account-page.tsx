"use client";

import { ArrowLeft } from "lucide-react";
import Link from "next/link";
import { useParams } from "next/navigation";

import { AccountDetail } from "@/components/finance/cash-accounts";
import { RequirePermission } from "@/components/panel/settings-nav";

export function AccountPage() {
  const { id } = useParams<{ id: string }>();
  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-4">
      <Link href="/panel/cari-kasa?sekme=kasa" className="flex min-h-11 items-center gap-1 self-start text-sm font-semibold text-black underline decoration-brand underline-offset-4">
        <ArrowLeft aria-hidden className="size-4" />
        Kasa ve banka
      </Link>
      <RequirePermission permission="viewFinance">
        <AccountDetail id={id} />
      </RequirePermission>
    </div>
  );
}
