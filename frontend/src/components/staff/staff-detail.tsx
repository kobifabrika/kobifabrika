"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import { ArrowLeft, Trash2 } from "lucide-react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { toast } from "sonner";

import { TabNav } from "@/components/contacts/contact-bits";
import { QueryError } from "@/components/feedback/query-error";
import { useSession } from "@/components/providers/session-provider";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { api, errorMessage, unwrap } from "@/lib/api/client";
import { can } from "@/lib/permissions";

import { StaffSwatch, staffKey, useStaffRefresh } from "./staff-bits";
import { StaffForm } from "./staff-form";
import { StaffHours } from "./staff-hours";
import { StaffServices } from "./staff-services";
import { StaffTimeOffs } from "./staff-time-offs";

const tabs = [
  { key: "bilgiler", label: "Bilgiler" },
  { key: "saatler", label: "Çalışma saatleri" },
  { key: "hizmetler", label: "Hizmetler" },
  { key: "kapali", label: "Kapalı zamanlar" },
] as const;

/** Personel kartı: bilgiler, haftalık saatler (öğle arası dahil), hizmetler, mola/izin. */
export function StaffDetail({ id }: { id: string }) {
  const { role } = useSession();
  const router = useRouter();
  const params = useSearchParams();
  const refresh = useStaffRefresh();
  const manage = can(role, "manageStaff");
  const tab = tabs.find((t) => t.key === params.get("sekme"))?.key ?? "bilgiler";
  const staff = useQuery({
    queryKey: [...staffKey, id],
    queryFn: () => unwrap(api.GET("/api/staff/{id}", { params: { path: { id } } })),
  });
  const remove = useMutation({
    mutationFn: () => unwrap(api.DELETE("/api/staff/{id}", { params: { path: { id } } })),
    onSuccess: async () => {
      await refresh();
      toast.success("Personel silindi.");
      router.push("/panel/personel");
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  if (staff.isPending)
    return (
      <p role="status" className="text-sm text-neutral-600">
        Yükleniyor…
      </p>
    );
  if (staff.isError) return <QueryError error={staff.error} onRetry={staff.refetch} />;
  const s = staff.data;

  return (
    <div className="flex flex-col gap-5">
      <Link href="/panel/personel" className="inline-flex min-h-11 items-center gap-2 self-start text-sm font-semibold text-black">
        <ArrowLeft aria-hidden className="size-4" />
        Personel
      </Link>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="flex items-center gap-3 text-2xl font-bold tracking-tight text-black">
          <StaffSwatch color={s.color} className="size-4" />
          {s.fullName}
          {!s.isActive && <Badge variant="outline">Pasif</Badge>}
        </h1>
        {manage && (
          <Button
            variant="ghost"
            className="text-danger-600"
            onClick={() => window.confirm(`${s.fullName} silinsin mi? Randevusu olan personel silinmez, pasife alınır.`) && remove.mutate()}
            disabled={remove.isPending}
          >
            <Trash2 aria-hidden />
            Sil
          </Button>
        )}
      </div>
      {s.title && <p className="-mt-3 text-sm text-neutral-600">{s.title}</p>}
      <TabNav label="Personel kartı" active={tab} tabs={tabs.map((t) => ({ ...t, href: `/panel/personel/${id}?sekme=${t.key}` }))} />
      {tab === "bilgiler" && <StaffForm key={s.id + String(s.isActive)} staff={s} readOnly={!manage} />}
      {tab === "saatler" && <StaffHours staffId={id} readOnly={!manage} />}
      {tab === "hizmetler" && <StaffServices staff={s} readOnly={!manage} />}
      {tab === "kapali" && <StaffTimeOffs staffId={id} readOnly={!manage} />}
    </div>
  );
}
