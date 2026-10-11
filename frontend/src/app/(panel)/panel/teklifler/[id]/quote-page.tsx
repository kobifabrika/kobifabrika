"use client";

import { useParams } from "next/navigation";

import { RequirePermission } from "@/components/panel/settings-nav";
import { QuoteDetail } from "@/components/quotes/quote-detail";
import { EditQuote } from "@/components/quotes/quote-form";

export function QuotePage() {
  const { id } = useParams<{ id: string }>();
  return <QuoteDetail id={id} />;
}

export function EditQuotePage() {
  const { id } = useParams<{ id: string }>();
  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-6">
      <h1 className="text-2xl font-bold tracking-tight text-black">Teklifi düzenle</h1>
      <RequirePermission permission="createSales">
        <EditQuote id={id} />
      </RequirePermission>
    </div>
  );
}
