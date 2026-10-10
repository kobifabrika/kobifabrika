"use client";

import { useSearchParams } from "next/navigation";

import { ContactForm } from "@/components/contacts/contact-form";
import { RequirePermission } from "@/components/panel/settings-nav";

/** ?tur=tedarikci ile açılırsa kayıt türü Tedarikçi seçili gelir (Cari ve Kasa → Yeni tedarikçi). */
export function NewContact() {
  const supplier = useSearchParams().get("tur") === "tedarikci";
  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-6">
      <h1 className="text-2xl font-bold tracking-tight text-black">{supplier ? "Yeni tedarikçi" : "Yeni müşteri"}</h1>
      <RequirePermission permission="writeContacts">
        <ContactForm supplier={supplier} />
      </RequirePermission>
    </div>
  );
}
