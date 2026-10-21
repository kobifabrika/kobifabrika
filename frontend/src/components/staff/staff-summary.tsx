"use client";

import { useQuery } from "@tanstack/react-query";
import type { ReactNode } from "react";

import { QueryError } from "@/components/feedback/query-error";
import { api, unwrap } from "@/lib/api/client";
import { staffColorNames, type Staff } from "@/lib/api/types";
import { displayPhone } from "@/lib/contacts";
import { formatDate } from "@/lib/format";

import { StaffSwatch, staffKey } from "./staff-bits";
import { useServices } from "./staff-services";

const dayNames: Record<string, string> = {
  Monday: "Pazartesi",
  Tuesday: "Salı",
  Wednesday: "Çarşamba",
  Thursday: "Perşembe",
  Friday: "Cuma",
  Saturday: "Cumartesi",
  Sunday: "Pazar",
};

function Rows({ rows }: { rows: [string, ReactNode][] }) {
  return (
    <dl className="flex max-w-xl flex-col divide-y divide-border border-y border-border">
      {rows.map(([label, value]) => (
        <div key={label} className="grid gap-1 py-3 sm:grid-cols-[12rem_1fr] sm:gap-4">
          <dt className="text-sm text-neutral-600">{label}</dt>
          <dd className="text-base text-black">{value ?? "—"}</dd>
        </div>
      ))}
    </dl>
  );
}

const loading = (
  <p role="status" className="text-sm text-neutral-600">
    Yükleniyor…
  </p>
);

/**
 * Personel kartının salt okunur görünümü (Mali Müşavir; 2026-10-21): devre dışı form yerine okunur özet. Boş alan "—".
 * Düzenleyebilen roller aynı sekmelerde formu görür (`StaffForm`, `StaffHours`, `StaffServices`).
 */
export function StaffInfoSummary({ staff: s }: { staff: Staff }) {
  return (
    <Rows
      rows={[
        ["Ad soyad", s.fullName],
        ["Unvan", s.title || null],
        ["Telefon", s.phone ? displayPhone(s.phone) : null],
        ["İşe başlama", s.startDate ? formatDate(s.startDate) : null],
        ["Doğum tarihi", s.birthDate ? formatDate(s.birthDate) : null],
        ["Not", s.notes || null],
        [
          "Takvim rengi",
          <span key="color" className="inline-flex items-center gap-2">
            <StaffSwatch color={s.color} className="size-4" />
            {staffColorNames[s.color]}
          </span>,
        ],
        ["Bağlı kullanıcı hesabı", s.userName || "Bağlı hesap yok"],
        ["Online randevuda", s.showInBooking ? "Görünür" : "Görünmez"],
        ["Durum", s.isActive ? "Aktif" : "Pasif"],
      ]}
    />
  );
}

export function StaffHoursSummary({ staffId }: { staffId: string }) {
  const hours = useQuery({
    queryKey: [...staffKey, staffId, "hours"],
    queryFn: () => unwrap(api.GET("/api/staff/{id}/working-hours", { params: { path: { id: staffId } } })),
  });
  if (hours.isPending) return loading;
  if (hours.isError) return <QueryError error={hours.error} onRetry={hours.refetch} />;
  return (
    <Rows
      rows={hours.data.days.map((d) => [
        dayNames[d.day] ?? d.day,
        d.ranges.length === 0 ? (
          <span className="text-neutral-600">Çalışmıyor</span>
        ) : (
          <span className="tabular-nums">{d.ranges.map((r) => `${r.start}–${r.end}`).join(", ")}</span>
        ),
      ])}
    />
  );
}

export function StaffServicesSummary({ staff }: { staff: Staff }) {
  const services = useServices();
  if (services.isPending) return loading;
  if (services.isError) return <QueryError error={services.error} onRetry={services.refetch} />;
  if (staff.serviceIds.length === 0) return <p className="text-sm text-black">Bütün hizmetleri verir.</p>;
  const names = services.data.filter((s) => staff.serviceIds.includes(s.id));
  return (
    <ul className="flex max-w-xl flex-col divide-y divide-border border-y border-border">
      {names.map((s) => (
        <li key={s.id} className="flex min-h-12 items-center justify-between gap-3 py-2 text-sm">
          <span className="text-black">{s.name}</span>
          <span className="text-neutral-600 tabular-nums">{s.durationMinutes ? `${s.durationMinutes} dk` : "Süre yok"}</span>
        </li>
      ))}
    </ul>
  );
}
