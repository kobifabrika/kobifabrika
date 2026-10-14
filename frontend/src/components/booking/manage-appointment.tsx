"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CalendarClock, Check, Phone, UserRound } from "lucide-react";
import Link from "next/link";
import { useEffect, useState } from "react";

import { FormMessage } from "@/components/forms/field";
import { PublicMessage } from "@/components/public/public-shell";
import { Button } from "@/components/ui/button";
import { ApiError, api, errorMessage, unwrap } from "@/lib/api/client";
import { appointmentStatusNames } from "@/lib/api/types";
import { longDayLabel } from "@/lib/calendar";
import { formatDateTime, istanbulParts } from "@/lib/format";

import { BookingShell } from "./booking-shell";

/**
 * Müşterinin randevu sayfası (/r/[slug]/randevu/[token]): randevu özeti; iptal süresi geçmediyse "Randevuyu iptal et" (onaylı),
 * geçtiyse "İptal için işletmeyi arayın" + telefon. İptal işletmeye bildirim olarak düşer.
 */
export function ManageAppointment({ token }: { token: string }) {
  const queryClient = useQueryClient();
  const key = ["public", "appointment", token];
  const [confirming, setConfirming] = useState(false);
  const page = useQuery({
    queryKey: key,
    queryFn: () => unwrap(api.GET("/api/public/appointments/{token}", { params: { path: { token } } })),
    retry: false,
  });
  const cancel = useMutation({
    mutationFn: () => unwrap(api.POST("/api/public/appointments/{token}/cancel", { params: { path: { token } } })),
    onSuccess: (data) => {
      queryClient.setQueryData(key, data);
      setConfirming(false);
    },
  });

  useEffect(() => {
    if (page.data) document.title = `${page.data.businessName} · Randevunuz`;
  }, [page.data]);

  if (page.isPending)
    return (
      <BookingShell>
        <p role="status" className="py-10 text-sm text-neutral-600">
          Yükleniyor…
        </p>
      </BookingShell>
    );
  if (page.isError)
    return (
      <BookingShell>
        <PublicMessage title="Randevu bulunamadı">
          {page.error instanceof ApiError && page.error.status === 404 ? "Linki kontrol edin." : errorMessage(page.error)}
        </PublicMessage>
      </BookingShell>
    );

  const a = page.data;
  const start = istanbulParts(a.startAt);
  const end = istanbulParts(a.endAt);
  const active = a.status === "Pending" || a.status === "Confirmed";
  return (
    <BookingShell name={a.businessName} logoUrl={a.logoUrl} address={[a.address]} phone={a.businessPhone} phoneE164={a.businessPhoneE164}>
      <div className="flex flex-col gap-1">
        <p className="text-base text-neutral-700">Sayın {a.contactName},</p>
        <h1 className="text-xl font-bold text-black">Randevunuz</h1>
      </div>

      {a.status === "Cancelled" ? (
        <section className="flex items-start gap-3 border-l-2 border-brand bg-neutral-50 px-4 py-4">
          <Check aria-hidden className="mt-0.5 size-5 shrink-0 text-black" />
          <p className="text-base text-black">Randevunuz iptal edildi.</p>
        </section>
      ) : (
        <p className="w-fit rounded-md border border-black px-2 py-1 text-sm font-semibold text-black">{appointmentStatusNames[a.status]}</p>
      )}

      <dl className="flex flex-col gap-3 text-base">
        <div className="flex items-start gap-3">
          <dt className="mt-0.5 shrink-0">
            <CalendarClock aria-hidden className="size-5" />
            <span className="sr-only">Gün ve saat</span>
          </dt>
          <dd className="font-semibold text-black tabular-nums">
            {longDayLabel(start.date)} · {start.time}-{end.time}
          </dd>
        </div>
        <div className="flex items-start gap-3">
          <dt className="mt-0.5 shrink-0">
            <UserRound aria-hidden className="size-5" />
            <span className="sr-only">Hizmet ve personel</span>
          </dt>
          <dd className="text-black">
            <span className="font-semibold">{a.serviceName}</span> · {a.staffName}
          </dd>
        </div>
      </dl>

      {active &&
        (a.canCancel ? (
          confirming ? (
            <div className="flex flex-col gap-3 border border-border p-4">
              <p className="text-base text-black">Randevunuz iptal edilsin mi?</p>
              {cancel.isError && <FormMessage>{errorMessage(cancel.error)}</FormMessage>}
              <div className="flex flex-wrap gap-2">
                <Button variant="destructive" onClick={() => cancel.mutate()} disabled={cancel.isPending}>
                  Evet, iptal et
                </Button>
                <Button variant="ghost" onClick={() => setConfirming(false)}>
                  Vazgeç
                </Button>
              </div>
            </div>
          ) : (
            <div className="flex flex-col gap-2">
              <Button variant="outline" className="self-start text-danger-600" onClick={() => setConfirming(true)}>
                Randevuyu iptal et
              </Button>
              {a.cancelDeadline && <p className="text-xs text-neutral-600">Online iptal için son an: {formatDateTime(a.cancelDeadline)}</p>}
            </div>
          )
        ) : (
          <div className="flex flex-col gap-2 border-l-2 border-brand bg-neutral-50 px-4 py-3">
            <p className="text-sm text-black">Randevuya az süre kaldığı için online iptal edilemez. İptal veya değişiklik için işletmeyi arayın.</p>
            {a.businessPhone && (
              <Button asChild variant="outline" className="self-start">
                <a href={`tel:${a.businessPhoneE164 ?? a.businessPhone.replace(/\s/g, "")}`}>
                  <Phone aria-hidden />
                  {a.businessPhone}
                </a>
              </Button>
            )}
          </div>
        ))}

      {a.status === "Cancelled" && (
        <Button asChild variant="outline" className="self-start">
          <Link href={`/r/${a.slug}`}>Yeni randevu al</Link>
        </Button>
      )}
    </BookingShell>
  );
}
