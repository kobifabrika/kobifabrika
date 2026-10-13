"use client";

import { IdCard, Plus } from "lucide-react";
import Link from "next/link";
import { useState } from "react";

import { EmptyState } from "@/components/contacts/contact-bits";
import { QueryError } from "@/components/feedback/query-error";
import { useSession } from "@/components/providers/session-provider";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Switch } from "@/components/ui/switch";
import { displayPhone } from "@/lib/contacts";
import { can } from "@/lib/permissions";

import { StaffSwatch, useStaffList } from "./staff-bits";

/** Personel listesi: masaüstünde tablo, telefonda kart. Sahip ve Yönetici ekler; Mali Müşavir salt okunur görür. */
export function StaffList() {
  const { role } = useSession();
  const [showInactive, setShowInactive] = useState(false);
  const staff = useStaffList(showInactive);
  const manage = can(role, "manageStaff");

  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-bold tracking-tight text-black">
          Personel {staff.data && <span className="text-base font-normal text-neutral-600 tabular-nums">({staff.data.length})</span>}
        </h1>
        {manage && (
          <Button asChild>
            <Link href="/panel/personel/yeni">
              <Plus aria-hidden />
              Personel ekle
            </Link>
          </Button>
        )}
      </div>
      <p className="max-w-2xl text-sm text-neutral-600">
        Takvimdeki kolonlar buradaki personeldir. Çalışma saatlerini, verdiği hizmetleri ve izin/mola gibi kapalı zamanlarını personelin
        kartından düzenleyin. Maaş, mesai ve izin talepleri ileride eklenecek.
      </p>
      <label className="flex min-h-11 items-center gap-3 self-start text-sm text-black">
        <Switch checked={showInactive} onCheckedChange={setShowInactive} />
        Pasif personeli de göster
      </label>

      {staff.isPending ? (
        <p role="status" className="text-sm text-neutral-600">
          Yükleniyor…
        </p>
      ) : staff.isError ? (
        <QueryError error={staff.error} onRetry={staff.refetch} />
      ) : staff.data.length === 0 ? (
        <EmptyState icon={<IdCard aria-hidden className="size-8" />} title="Henüz personel yok">
          <p className="text-sm text-neutral-600">Randevu verebilmek için en az bir personel ekleyin (işletme sahibi de olabilir).</p>
          {manage && (
            <Button asChild>
              <Link href="/panel/personel/yeni">Personel ekle</Link>
            </Button>
          )}
        </EmptyState>
      ) : (
        <>
          <table className="hidden w-full text-sm md:table">
            <thead>
              <tr className="border-b border-black text-left text-xs text-neutral-600">
                <th className="py-2 pr-3 font-semibold">Ad soyad</th>
                <th className="py-2 pr-3 font-semibold">Unvan</th>
                <th className="py-2 pr-3 font-semibold">Telefon</th>
                <th className="py-2 pr-3 font-semibold">Hizmetler</th>
                <th className="py-2 pr-3 font-semibold">Online randevu</th>
                <th className="py-2 font-semibold">Hesap</th>
              </tr>
            </thead>
            <tbody>
              {staff.data.map((s) => (
                <tr key={s.id} className="border-b border-border">
                  <td className="py-2 pr-3">
                    <Link href={`/panel/personel/${s.id}`} className="inline-flex min-h-11 items-center gap-2 font-semibold text-black underline-offset-4 hover:underline">
                      <StaffSwatch color={s.color} />
                      {s.fullName}
                    </Link>
                    {!s.isActive && (
                      <Badge variant="outline" className="ml-2">
                        Pasif
                      </Badge>
                    )}
                  </td>
                  <td className="py-2 pr-3 text-neutral-700">{s.title}</td>
                  <td className="py-2 pr-3 text-neutral-700 tabular-nums">{displayPhone(s.phone)}</td>
                  <td className="py-2 pr-3 text-neutral-700">{s.allServices ? "Hepsi" : `${s.serviceIds.length} hizmet`}</td>
                  <td className="py-2 pr-3 text-neutral-700">{s.showInBooking ? "Görünür" : "Gizli"}</td>
                  <td className="py-2 text-neutral-700">{s.userName ?? "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <ul className="flex flex-col divide-y divide-border border-y border-border md:hidden">
            {staff.data.map((s) => (
              <li key={s.id}>
                <Link href={`/panel/personel/${s.id}`} className="flex min-h-14 flex-col gap-1 py-3">
                  <span className="flex items-center gap-2 font-semibold text-black">
                    <StaffSwatch color={s.color} />
                    {s.fullName}
                    {!s.isActive && <Badge variant="outline">Pasif</Badge>}
                  </span>
                  <span className="text-sm text-neutral-600">
                    {[s.title, s.allServices ? "Bütün hizmetler" : `${s.serviceIds.length} hizmet`, s.userName].filter(Boolean).join(" · ")}
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        </>
      )}
    </div>
  );
}
