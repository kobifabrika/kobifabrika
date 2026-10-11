"use client";

import { useParams } from "next/navigation";

import { ReceiptView } from "@/components/sales/receipt-view";
import { SaleDetail } from "@/components/sales/sale-detail";

export function SalePage() {
  const { id } = useParams<{ id: string }>();
  return <SaleDetail id={id} />;
}

export function ReceiptPage() {
  const { id } = useParams<{ id: string }>();
  return <ReceiptView id={id} />;
}
