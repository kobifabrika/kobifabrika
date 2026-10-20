"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { cn } from "cn";
import { CalendarDays, ChevronLeft, ChevronRight, Plus } from "lucide-react";
import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useEffect, useMemo, useRef, useState, type TouchEvent } from "react";
import { toast } from "sonner";

import { EmptyState } from "@/components/contacts/contact-bits";
import { QueryError } from "@/components/feedback/query-error";
import { useSession } from "@/components/providers/session-provider";
import { StaffSwatch, staffKey, useStaffList } from "@/components/staff/staff-bits";
import { Button } from "@/components/ui/button";
import { ScrollRow } from "@/components/ui/scroll-row";
import { emptySlotCampaignHref } from "@/components/marketing/suggestions";
import { Switch } from "@/components/ui/switch";
import { api, errorMessage, unwrap } from "@/lib/api/client";
import type { Appointment, Calendar, StaffColor } from "@/lib/api/types";
import { dayLabel, dayOf, gridBounds, longDayLabel, minutesOf, toIso } from "@/lib/calendar";
import { addDays, startOfWeek } from "@/lib/date-ranges";
import { istanbulParts, todayIstanbul } from "@/lib/format";
import { can } from "@/lib/permissions";
import { useIsDesktop } from "@/lib/use-media";

import { StatusBadge, calendarKey, useAppointmentRefresh } from "./appointment-bits";
import { AppointmentForm, hasMobile, type AppointmentDraft } from "./appointment-form";
import { AppointmentSheet } from "./appointment-sheet";
import { TomorrowNotices } from "./tomorrow-notices";
import { useSmsEnabled, type WhatsAppKind } from "./whatsapp-notice";
import { TimeGrid, type GridColumn, type MoveTarget } from "./time-grid";

type View = "gun" | "hafta";

function useNowMinute() {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), 60_000);
    return () => clearInterval(timer);
  }, []);
  return { today: todayIstanbul(now), minute: minutesOf(now.toISOString()) };
}

/**
 * Takvim (/panel/randevular). Adres: ?gorunum=gun|hafta&tarih=2026-10-14&personel={id}&randevu={id}&yeni=1.
 * Masaüstü gün görünümü personel kolonlu; hafta görünümü tek personelin 7 günü. Telefonda tek personel kolonu + personel
 * seçici, gün okla veya kaydırarak değişir; hafta görünümü gün gün liste. Sürükle-bırak sadece masaüstünde; telefonda "Taşı".
 */
export function CalendarView() {
  const { role, me } = useSession();
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();
  const desktop = useIsDesktop();
  const queryClient = useQueryClient();
  const refresh = useAppointmentRefresh();
  const now = useNowMinute();
  const write = can(role, "writeAppointments");
  const sms = useSmsEnabled();
  /** SMS kapalıyken taşıma sonrası açılan randevu penceresinde "Yeni saati WhatsApp'tan bildir". */
  const [notice, setNotice] = useState<WhatsAppKind | null>(null);

  const view: View = params.get("gorunum") === "hafta" ? "hafta" : "gun";
  const date = params.get("tarih") ?? now.today;
  const from = view === "hafta" ? startOfWeek(date) : date;
  const to = view === "hafta" ? addDays(from, 6) : date;
  const [showCancelled, setShowCancelled] = useState(false);
  const [form, setForm] = useState<{ draft?: AppointmentDraft; appointment?: Appointment } | null>(
    params.get("yeni") === "1" ? { draft: { date, contactId: params.get("musteri") } } : null,
  );

  const setParams = (next: Record<string, string | null>) => {
    const p = new URLSearchParams(params.toString());
    for (const [k, v] of Object.entries(next)) {
      if (v === null) p.delete(k);
      else p.set(k, v);
    }
    router.replace(`${pathname}?${p.toString()}`, { scroll: false });
  };

  const key = [...calendarKey, from, to] as const;
  const calendar = useQuery({
    queryKey: key,
    queryFn: () => unwrap(api.GET("/api/calendar", { params: { query: { from, to } } })) as Promise<Calendar>,
  });
  const staffList = useStaffList();

  const columnsStaff = useMemo(() => calendar.data?.staff ?? [], [calendar.data]);
  const selectedParam = params.get("personel");
  const defaultStaff = columnsStaff.find((s) => s.id === calendar.data?.myStaffId) ?? columnsStaff[0];
  const selected =
    columnsStaff.find((s) => s.id === selectedParam) ?? (desktop && view === "gun" ? null : defaultStaff);
  const staffColors = useMemo(
    () => Object.fromEntries(columnsStaff.map((s) => [s.id, s.color])) as Record<string, StaffColor>,
    [columnsStaff],
  );

  const move = useMutation({
    mutationFn: ({ appointment, target }: { appointment: Appointment; target: MoveTarget }) =>
      unwrap(
        api.PUT("/api/appointments/{id}", {
          params: { path: { id: appointment.id } },
          body: {
            staffMemberId: target.staffId,
            productId: appointment.productId,
            startAt: toIso(target.date, target.start),
            endAt: toIso(target.date, target.end),
            note: appointment.note,
            customerPackageId: appointment.customerPackageId,
            notifyCustomer: false,
          },
        }),
      ),
    onMutate: async ({ appointment, target }) => {
      await queryClient.cancelQueries({ queryKey: key });
      const previous = queryClient.getQueryData<Calendar>(key);
      queryClient.setQueryData<Calendar>(
        key,
        (old) =>
          old && {
            ...old,
            appointments: old.appointments.map((a) =>
              a.id === appointment.id
                ? {
                    ...a,
                    staffMemberId: target.staffId,
                    startAt: toIso(target.date, target.start),
                    endAt: toIso(target.date, target.end),
                  }
                : a,
            ),
          },
      );
      return { previous };
    },
    onError: (e, _, ctx) => {
      if (ctx?.previous) queryClient.setQueryData(key, ctx.previous);
      toast.error(errorMessage(e));
    },
    onSuccess: (saved, { appointment }) => {
      toast.success("Randevu taşındı.");
      for (const w of saved.warnings) toast.warning(w);
      // Saat değiştiyse ve cep telefonu varsa: "Müşteriye SMS ile bildirilsin mi? [Gönder]" (online randevuda kapanmaz).
      const timeChanged =
        saved.appointment.startAt !== appointment.startAt || saved.appointment.endAt !== appointment.endAt;
      if (timeChanged && hasMobile(appointment.contactPhone) && !sms) {
        toast("Müşteriye yeni saati bildirin.", {
          id: `notify-${appointment.id}`,
          description: `${appointment.contactName} · WhatsApp'tan`,
          duration: 15_000,
          action: {
            label: "Bildir",
            onClick: () => {
              setNotice("Moved");
              setParams({ randevu: appointment.id });
            },
          },
        });
      } else if (timeChanged && hasMobile(appointment.contactPhone)) {
        const online = appointment.source === "Online";
        toast(
          online ? "Online randevu taşındı. Müşteriye SMS ile bildirilsin mi?" : "Müşteriye SMS ile bildirilsin mi?",
          {
            id: `notify-${appointment.id}`,
            description: `${appointment.contactName} · yeni saat ve iptal linki`,
            duration: online ? Infinity : 10_000,
            closeButton: online,
            action: { label: "Gönder", onClick: () => notifyMoved.mutate(appointment.id) },
          },
        );
      }
    },
    onSettled: () => refresh(),
  });

  const notifyMoved = useMutation({
    mutationFn: (id: string) => unwrap(api.POST("/api/appointments/{id}/notify-moved", { params: { path: { id } } })),
    onSuccess: (r) =>
      r.status === "Sent"
        ? toast.success("Müşteriye SMS ile bildirildi.")
        : r.status === "NoCredit"
          ? toast.error("SMS gönderilemedi: krediniz yetersiz.")
          : toast.error("SMS gönderilemedi."),
    onError: (e) => toast.error(errorMessage(e)),
  });

  const addMe = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/staff", {
          body: {
            fullName: me.user.fullName,
            title: null,
            phone: null,
            color: "Black",
            showInBooking: true,
            userId: me.user.id,
            isActive: true,
          },
        }),
      ),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: staffKey }),
        queryClient.invalidateQueries({ queryKey: calendarKey }),
      ]);
      toast.success("Personel olarak eklendiniz. Çalışma saatleriniz işletmenin saatleri.");
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  const shift = (days: number) => setParams({ tarih: addDays(date, view === "hafta" ? days * 7 : days) });
  const touch = useRef<{ x: number; y: number } | null>(null);
  const onTouchStart = (e: TouchEvent) => (touch.current = { x: e.touches[0]!.clientX, y: e.touches[0]!.clientY });
  const onTouchEnd = (e: TouchEvent) => {
    const start = touch.current;
    touch.current = null;
    if (!start || desktop) return;
    const dx = e.changedTouches[0]!.clientX - start.x;
    const dy = e.changedTouches[0]!.clientY - start.y;
    if (Math.abs(dx) > 70 && Math.abs(dy) < 50) shift(dx < 0 ? 1 : -1);
  };

  const data = calendar.data;
  const openId = params.get("randevu");
  const openInitial = data?.appointments.find((a) => a.id === openId);

  let columns: GridColumn[] = [];
  if (data) {
    if (view === "gun") {
      const day = data.days[0]!;
      const shown = desktop && !selected ? columnsStaff : selected ? [selected] : [];
      columns = shown.map((s) => ({
        key: `${s.id}_${day.date}`,
        staffId: s.id,
        date: day.date,
        title: s.fullName,
        subtitle: s.title ?? undefined,
        color: s.color,
        ranges: s.days.find((d) => d.date === day.date)?.ranges ?? [],
        closedLabel: day.holidayName ?? (day.closed ? "Kapalı" : null),
      }));
    } else if (selected) {
      columns = data.days.map((day) => ({
        key: `${selected.id}_${day.date}`,
        staffId: selected.id,
        date: day.date,
        title: dayLabel(day.date),
        ranges: selected.days.find((d) => d.date === day.date)?.ranges ?? [],
        closedLabel: day.holidayName ?? (day.closed ? "Kapalı" : null),
      }));
    }
  }
  const visibleAppointments = (data?.appointments ?? []).filter((a) =>
    columns.some((c) => c.staffId === a.staffMemberId),
  );
  const bounds = gridBounds(
    data?.days ?? [],
    visibleAppointments.map((a) => ({
      start: minutesOf(a.startAt),
      end: minutesOf(a.startAt) + (Date.parse(a.endAt) - Date.parse(a.startAt)) / 60000,
    })),
  );
  const pending = (data?.appointments ?? []).filter((a) => a.status === "Pending").length;
  const title = view === "hafta" ? `${dayLabel(from)} – ${dayLabel(to)}` : longDayLabel(date);

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-bold tracking-tight text-black">Randevular</h1>
        <div className="flex flex-wrap items-center gap-2">
          {pending > 0 && (
            <span className="inline-flex min-h-8 items-center rounded-md border border-dashed border-black px-2 text-sm font-semibold text-black">
              Onay bekleyen {pending}
            </span>
          )}
          {write && columnsStaff.length > 0 && (
            <Button onClick={() => setForm({ draft: { date, staffId: selected?.id } })}>
              <Plus aria-hidden />
              Yeni randevu
            </Button>
          )}
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <div className="flex items-center">
          <Button
            variant="outline"
            size="icon"
            aria-label={view === "hafta" ? "Önceki hafta" : "Önceki gün"}
            onClick={() => shift(-1)}
          >
            <ChevronLeft aria-hidden />
          </Button>
          <Button
            variant="outline"
            size="icon"
            aria-label={view === "hafta" ? "Sonraki hafta" : "Sonraki gün"}
            onClick={() => shift(1)}
          >
            <ChevronRight aria-hidden />
          </Button>
        </div>
        <Button variant="outline" onClick={() => setParams({ tarih: null })} disabled={date === now.today}>
          Bugün
        </Button>
        <label className="sr-only" htmlFor="takvim-tarih">
          Tarih seç
        </label>
        <input
          id="takvim-tarih"
          type="date"
          value={date}
          onChange={(e) => e.target.value && setParams({ tarih: e.target.value })}
          className="h-11 rounded-lg border border-neutral-400 px-2 text-base"
        />
        <div role="group" aria-label="Görünüm" className="flex">
          {(["gun", "hafta"] as const).map((v) => (
            <button
              key={v}
              type="button"
              aria-pressed={view === v}
              onClick={() => setParams({ gorunum: v === "gun" ? null : v })}
              className={cn(
                "min-h-11 border px-3 text-sm font-semibold first:rounded-l-lg last:rounded-r-lg",
                view === v
                  ? "border-black bg-black text-white"
                  : "border-border bg-white text-black hover:border-black",
              )}
            >
              {v === "gun" ? "Gün" : "Hafta"}
            </button>
          ))}
        </div>
        <label className="flex min-h-11 items-center gap-2 text-sm text-black">
          <Switch checked={showCancelled} onCheckedChange={setShowCancelled} />
          İptalleri göster
        </label>
      </div>

      <p className="text-base font-semibold text-black" aria-live="polite">
        {title}
      </p>
      {view !== "hafta" && date === addDays(now.today, 1) && sms && can(role, "manageMarketing") && (
        <EmptySlotStrip date={date} />
      )}
      {view !== "hafta" && date === addDays(now.today, 1) && !sms && (
        <details className="group border border-border">
          <summary className="flex min-h-11 cursor-pointer items-center px-3 text-sm font-semibold text-black">
            Yarının hatırlatmaları (WhatsApp)
          </summary>
          <div className="p-2">
            <TomorrowNotices />
          </div>
        </details>
      )}

      {columnsStaff.length > 1 && (!desktop || view === "hafta") && (
        <ScrollRow role="radiogroup" aria-label="Personel">
          {columnsStaff.map((s) => (
            <button
              key={s.id}
              type="button"
              role="radio"
              aria-checked={selected?.id === s.id}
              onClick={() => setParams({ personel: s.id })}
              className={cn(
                "flex min-h-11 shrink-0 items-center gap-2 rounded-lg border px-3 text-sm font-semibold",
                selected?.id === s.id ? "border-black bg-black text-white" : "border-border bg-white text-black",
              )}
            >
              <StaffSwatch color={s.color} />
              {s.fullName}
            </button>
          ))}
        </ScrollRow>
      )}
      {desktop && view === "gun" && selectedParam && columnsStaff.length > 1 && (
        <Button variant="ghost" className="self-start" onClick={() => setParams({ personel: null })}>
          Bütün personeli göster
        </Button>
      )}

      {calendar.isPending ? (
        <p role="status" className="text-sm text-neutral-600">
          Yükleniyor…
        </p>
      ) : calendar.isError ? (
        <QueryError error={calendar.error} onRetry={calendar.refetch} />
      ) : columnsStaff.length === 0 ? (
        <EmptyState
          icon={<CalendarDays aria-hidden className="size-8" />}
          title={data!.onlyOwn ? "Hesabınız bir personele bağlı değil" : "Takvimde personel yok"}
        >
          <p className="text-sm text-neutral-600">
            {data!.onlyOwn
              ? "Sadece kendi randevularınızı görürsünüz. İşletme sahibinden hesabınızı bir personele bağlamasını isteyin."
              : "Randevu vermek için en az bir personel ekleyin. Kendiniz çalışıyorsanız kendinizi ekleyebilirsiniz."}
          </p>
          {can(role, "manageStaff") && (
            <div className="flex flex-wrap gap-2">
              <Button onClick={() => addMe.mutate()} disabled={addMe.isPending}>
                Kendimi personel olarak ekle
              </Button>
              <Button asChild variant="outline">
                <Link href="/panel/personel/yeni">Personel ekle</Link>
              </Button>
            </div>
          )}
        </EmptyState>
      ) : !desktop && view === "hafta" ? (
        <WeekAgenda
          days={data!.days.map((d) => d.date)}
          appointments={visibleAppointments.filter((a) => showCancelled || a.status !== "Cancelled")}
          onOpen={(a) => setParams({ randevu: a.id })}
          onDay={(d) => setParams({ gorunum: null, tarih: d })}
        />
      ) : (
        <div onTouchStart={onTouchStart} onTouchEnd={onTouchEnd}>
          <TimeGrid
            columns={columns}
            appointments={visibleAppointments}
            timeOffs={data!.timeOffs}
            bounds={bounds}
            today={now.today}
            nowMinute={now.minute}
            canWrite={write}
            draggable={desktop}
            showCancelled={showCancelled}
            staffColors={staffColors}
            onCreate={(c, minute) =>
              setForm({
                draft: {
                  staffId: c.staffId,
                  date: c.date,
                  time: `${String(Math.floor(minute / 60)).padStart(2, "0")}:${String(minute % 60).padStart(2, "0")}`,
                },
              })
            }
            onOpen={(a) => setParams({ randevu: a.id })}
            onMove={(appointment, target) => move.mutate({ appointment, target })}
          />
        </div>
      )}

      {form && staffList.data && (
        <AppointmentForm
          open
          onOpenChange={(o) => {
            if (!o) {
              setForm(null);
              if (params.get("yeni")) setParams({ yeni: null, musteri: null });
            }
          }}
          draft={form.draft}
          appointment={form.appointment}
          staff={staffList.data.filter((s) => !data?.onlyOwn || s.id === data.myStaffId)}
          onSaved={(saved) => {
            const d = dayOf(saved.appointment.startAt);
            const before = form.appointment;
            const moved =
              !!before && (before.startAt !== saved.appointment.startAt || before.endAt !== saved.appointment.endAt);
            if (moved && !sms && hasMobile(before.contactPhone)) {
              setNotice("Moved");
              setParams({ tarih: d, yeni: null, randevu: saved.appointment.id });
            } else if (d < from || d > to) setParams({ tarih: d, yeni: null });
          }}
        />
      )}
      <AppointmentSheet
        appointmentId={openId}
        initial={openInitial}
        notice={notice}
        onClose={() => {
          setNotice(null);
          setParams({ randevu: null });
        }}
        onMove={(a) => {
          setParams({ randevu: null });
          setForm({ appointment: a });
        }}
      />
    </div>
  );
}

/** Telefonda hafta görünümü: gün gün randevu listesi. */
function WeekAgenda({
  days,
  appointments,
  onOpen,
  onDay,
}: {
  days: string[];
  appointments: Appointment[];
  onOpen: (a: Appointment) => void;
  onDay: (day: string) => void;
}) {
  return (
    <ul className="flex flex-col divide-y divide-border border-y border-border">
      {days.map((d) => {
        const items = appointments
          .filter((a) => dayOf(a.startAt) === d)
          .sort((a, b) => a.startAt.localeCompare(b.startAt));
        return (
          <li key={d} className="flex flex-col gap-1 py-3">
            <button
              type="button"
              onClick={() => onDay(d)}
              className="flex min-h-11 items-center text-left text-sm font-semibold text-black underline-offset-4 hover:underline"
            >
              {longDayLabel(d)}
            </button>
            {items.length === 0 ? (
              <span className="text-sm text-neutral-600">Randevu yok</span>
            ) : (
              items.map((a) => (
                <button
                  key={a.id}
                  type="button"
                  onClick={() => onOpen(a)}
                  className="flex min-h-11 items-center gap-3 rounded-md px-1 text-left hover:bg-neutral-100"
                >
                  <span className="w-12 text-sm font-semibold text-black tabular-nums">
                    {istanbulParts(a.startAt).time}
                  </span>
                  <span className="flex min-w-0 flex-1 flex-col">
                    <span className="truncate text-sm font-semibold text-black">{a.contactName}</span>
                    <span className="truncate text-xs text-neutral-600">{a.serviceName}</span>
                  </span>
                  <StatusBadge status={a.status} />
                </button>
              ))
            )}
          </li>
        );
      })}
    </ul>
  );
}

/** Yarının gün görünümünde boş saat önerisi: "Yarın 14:00-16:00 Ayşe Usta boş · Kampanya" (Sahip, Yönetici). */
function EmptySlotStrip({ date }: { date: string }) {
  const slots = useQuery({
    queryKey: ["marketing", "empty-slots", date],
    queryFn: () => unwrap(api.GET("/api/marketing/empty-slots", { params: { query: { date } } })),
    staleTime: 60_000,
  });
  const items = slots.data?.items ?? [];
  if (items.length === 0) return null;
  return (
    <section
      aria-label="Boş saat önerisi"
      className="flex flex-col gap-2 border-l-2 border-brand bg-neutral-50 px-3 py-2"
    >
      {items.map((s) => (
        <div key={`${s.staffId}-${s.start}`} className="flex flex-wrap items-center justify-between gap-x-3 gap-y-1">
          <span className="text-sm text-black">
            Yarın{" "}
            <span className="font-semibold tabular-nums">
              {s.start}-{s.end}
            </span>{" "}
            {s.staffName} boş
          </span>
          <Link
            href={emptySlotCampaignHref(date, s)}
            className="inline-flex min-h-11 items-center gap-1 text-sm font-semibold text-black underline decoration-brand underline-offset-4"
          >
            Kampanya başlat
          </Link>
        </div>
      ))}
    </section>
  );
}
