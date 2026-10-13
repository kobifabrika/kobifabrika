"use client";

import { useParams } from "next/navigation";

import { StaffDetail } from "@/components/staff/staff-detail";

export function StaffPage() {
  const { id } = useParams<{ id: string }>();
  return <StaffDetail id={id} />;
}
