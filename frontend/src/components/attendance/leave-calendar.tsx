"use client";

import { useQuery } from "@tanstack/react-query";
import { cn } from "cn";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { useRouter, useSearchParams } from "next/navigation";

import { QueryError } from "@/components/feedback/query-error";
import { Button } from "@/components/ui/button";
import { api, unwrap } from "@/lib/api/client";
import { halfDayNames, type LeaveCalendar, type LeaveCalendarItem } from "@/lib/api/types";
import { dayLabel } from "@/lib/calendar";
import { todayIstanbul } from "@/lib/format";
import { addMonths, inRange, monthLabel } from "@/lib/time-clock";
import { useIsDesktop } from "@/lib/use-media";

import { leaveKey } from "./attendance-bits";

const weekday = new Intl.DateTimeFormat("tr-TR", { timeZone: "UTC", weekday: "narrow" });

function itemText(item: LeaveCalendarItem): string {
  const type = item.typeName ?? "İzinli";
  const half = item.halfDay === "None" ? "" : ` (${halfDayNames[item.halfDay].toLocaleLowerCase("tr-TR")})`;
  return `${type}${half}${item.status === "Pending" ? " · onay bekliyor" : ""}`;
}

/**
 * İzin takvimi (ay görünümü): kim ne zaman izinli. Masaüstünde personel satırları × gün kolonları (kapalı gün ve tatil gri,
 * onaylı izin siyah, bekleyen çerçeveli, yarım gün yarım dolu); telefonda gün gün liste. Personel rolü başkalarının türünü görmez.
 */
export function LeaveCalendarView() {
  const router = useRouter();
  const params = useSearchParams();
  const desktop = useIsDesktop();
  const current = todayIstanbul().slice(0, 7);
  const month = /^\d{4}-\d{2}$/.test(params.get("ay") ?? "") ? params.get("ay")! : current;
  const calendar = useQuery({
    queryKey: [...leaveKey, "calendar", month],
    queryFn: () => unwrap(api.GET("/api/leave-calendar", { params: { query: { month } } })),
  });

  const go = (m: string) => {
    const next = new URLSearchParams(params.toString());
    next.set("sekme", "takvim");
    if (m === current) next.delete("ay");
    else next.set("ay", m);
    router.replace(`?${next.toString()}`, { scroll: false });
  };

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center gap-2">
        <Button variant="outline" size="icon" aria-label="Önceki ay" onClick={() => go(addMonths(month, -1))}>
          <ChevronLeft aria-hidden />
        </Button>
        <h2 className="min-w-36 text-center text-base font-semibold text-black">{monthLabel(month)}</h2>
        <Button variant="outline" size="icon" aria-label="Sonraki ay" onClick={() => go(addMonths(month, 1))}>
          <ChevronRight aria-hidden />
        </Button>
      </div>
      {calendar.isPending ? (
        <p role="status" className="text-sm text-neutral-600">
          Yükleniyor…
        </p>
      ) : calendar.isError ? (
        <QueryError error={calendar.error} onRetry={calendar.refetch} />
      ) : desktop ? (
        <MonthGrid data={calendar.data} />
      ) : (
        <DayList data={calendar.data} />
      )}
    </div>
  );
}

function MonthGrid({ data }: { data: LeaveCalendar }) {
  const today = todayIstanbul();
  return (
    <div className="flex flex-col gap-3">
      {/* Klavyeyle de yatay kaydırılabilsin (axe: scrollable-region-focusable). */}
      <div className="overflow-x-auto border border-border" tabIndex={0} role="region" aria-label="İzin takvimi ızgarası">
        <table className="w-full border-collapse text-xs">
          <thead>
            <tr>
              <th scope="col" className="sticky left-0 z-10 min-w-36 border-b border-border bg-white px-2 py-2 text-left text-sm font-semibold text-black">
                Personel
              </th>
              {data.days.map((d) => (
                <th
                  key={d.date}
                  scope="col"
                  title={d.holidayName ?? undefined}
                  className={cn(
                    "min-w-7 border-b border-l border-border px-0.5 py-1 text-center font-normal tabular-nums",
                    d.closed || d.holidayName ? "bg-neutral-100 text-neutral-600" : "text-black",
                    d.date === today && "shadow-[inset_0_-3px_0_var(--color-brand)]",
                  )}
                >
                  <span className="block">{weekday.format(new Date(`${d.date}T00:00:00Z`))}</span>
                  <span className="block font-semibold">{Number(d.date.slice(8))}</span>
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {data.staff.map((s) => (
              <tr key={s.id}>
                <th scope="row" className="sticky left-0 z-10 border-b border-border bg-white px-2 py-1 text-left text-sm font-normal text-black">
                  {s.fullName}
                </th>
                {data.days.map((d) => {
                  const item = data.items.find((i) => i.staffMemberId === s.id && inRange(d.date, i.startDate, i.endDate));
                  const closed = d.closed || !!d.holidayName;
                  return (
                    <td
                      key={d.date}
                      title={item ? `${s.fullName}: ${itemText(item)}` : (d.holidayName ?? undefined)}
                      className={cn("h-9 border-b border-l border-border p-0.5", closed && !item && "bg-neutral-100")}
                    >
                      {item && (
                        <span
                          role="img"
                          aria-label={`${s.fullName} ${dayLabel(d.date)}: ${itemText(item)}`}
                          className={cn(
                            "block h-full",
                            item.status === "Pending" ? "border border-dashed border-black" : "bg-black",
                            item.halfDay !== "None" && "w-1/2",
                            item.halfDay === "Afternoon" && "ml-auto",
                          )}
                        />
                      )}
                    </td>
                  );
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <ul className="flex flex-wrap gap-4 text-xs text-neutral-700">
        <li className="flex items-center gap-1.5">
          <span aria-hidden className="inline-block size-3 bg-black" /> İzinli
        </li>
        <li className="flex items-center gap-1.5">
          <span aria-hidden className="inline-block size-3 border border-dashed border-black" /> Onay bekliyor
        </li>
        <li className="flex items-center gap-1.5">
          <span aria-hidden className="inline-block size-3 bg-neutral-100 ring-1 ring-border" /> Kapalı gün / tatil
        </li>
      </ul>
    </div>
  );
}

function DayList({ data }: { data: LeaveCalendar }) {
  const names = new Map(data.staff.map((s) => [s.id, s.fullName]));
  const days = data.days
    .map((d) => ({ day: d, items: data.items.filter((i) => inRange(d.date, i.startDate, i.endDate)) }))
    .filter((x) => x.items.length > 0 || x.day.holidayName);
  if (days.length === 0) return <p className="text-sm text-neutral-600">Bu ay izinli kimse yok.</p>;
  return (
    <ul className="flex flex-col divide-y divide-border border-y border-border">
      {days.map(({ day, items }) => (
        <li key={day.date} className="flex flex-col gap-1 py-2">
          <span className="text-sm font-semibold text-black">
            {dayLabel(day.date)}
            {day.holidayName ? ` · ${day.holidayName}` : ""}
          </span>
          {items.map((i) => (
            <span key={i.requestId} className="text-sm text-black">
              {names.get(i.staffMemberId) ?? ""} · <span className="text-neutral-700">{itemText(i)}</span>
            </span>
          ))}
        </li>
      ))}
    </ul>
  );
}
