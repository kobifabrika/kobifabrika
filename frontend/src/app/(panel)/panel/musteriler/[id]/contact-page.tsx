"use client";

import { useParams } from "next/navigation";

import { ContactDetail } from "@/components/contacts/contact-detail";
import { EditContact } from "@/components/contacts/contact-form";
import { RequirePermission } from "@/components/panel/settings-nav";

/** Müşteri ve tedarikçi kartı aynı bileşen; sadece adres ve geri linki farklı. */
export function ContactPage({ basePath }: { basePath: string }) {
  const { id } = useParams<{ id: string }>();
  return <ContactDetail id={id} basePath={basePath} />;
}

export function EditContactPage() {
  const { id } = useParams<{ id: string }>();
  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-6">
      <h1 className="text-2xl font-bold tracking-tight text-black">Kartı düzenle</h1>
      <RequirePermission permission="writeContacts">
        <EditContact id={id} />
      </RequirePermission>
    </div>
  );
}
