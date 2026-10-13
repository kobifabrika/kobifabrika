import type { Metadata } from "next";
import Link from "next/link";

import { RequirePermission } from "@/components/panel/settings-nav";
import { StaffForm } from "@/components/staff/staff-form";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Personel ekle" };

export default function NewStaffPage() {
  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-6">
      <Link href="/panel/personel" className="inline-flex min-h-11 items-center self-start text-sm font-semibold text-black">
        ← Personel
      </Link>
      <h1 className="text-2xl font-bold tracking-tight text-black">Personel ekle</h1>
      <RequirePermission permission="manageStaff">
        <StaffForm />
      </RequirePermission>
    </div>
  );
}
