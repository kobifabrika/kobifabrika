"use client";

import { CopyCheck } from "lucide-react";
import { cn } from "cn";

import { Button } from "@/components/ui/button";
import { Switch } from "@/components/ui/switch";
import type { WorkingHour } from "@/lib/api/types";

export const dayNames: Record<WorkingHour["day"], string> = {
  Monday: "Pazartesi",
  Tuesday: "Salı",
  Wednesday: "Çarşamba",
  Thursday: "Perşembe",
  Friday: "Cuma",
  Saturday: "Cumartesi",
  Sunday: "Pazar",
};

const weekOrder: WorkingHour["day"][] = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

/** Varsayılan hafta: Pzt-Cmt 09:00-19:00, Pazar kapalı (backend WorkingHour.DefaultWeek ile aynı). */
export function defaultWeek(): WorkingHour[] {
  return weekOrder.map((day) => ({ day, isClosed: day === "Sunday", opensAt: "09:00", closesAt: "19:00" }));
}

/** Açık günlerde kapanış açılıştan sonra olmalı; hatalı günlerin adları döner. */
export function invalidDays(days: WorkingHour[]): string[] {
  return days.filter((d) => !d.isClosed && d.opensAt >= d.closesAt).map((d) => dayNames[d.day]);
}

type Props = {
  value: WorkingHour[];
  onChange: (value: WorkingHour[]) => void;
  disabled?: boolean;
};

/** Haftalık çalışma saatleri: her gün aç/kapa + açılış/kapanış; "Tüm günlere uygula". Telefonda tek sütun. */
export function WorkingHoursEditor({ value, onChange, disabled }: Props) {
  const days = weekOrder.map((day) => value.find((d) => d.day === day) ?? { day, isClosed: true, opensAt: "09:00", closesAt: "19:00" });

  const update = (day: WorkingHour["day"], patch: Partial<WorkingHour>) =>
    onChange(days.map((d) => (d.day === day ? { ...d, ...patch } : d)));

  const first = days.find((d) => !d.isClosed);
  const applyToAll = () => {
    if (!first) return;
    onChange(days.map((d) => (d.isClosed ? d : { ...d, opensAt: first.opensAt, closesAt: first.closesAt })));
  };

  return (
    <div className="flex flex-col gap-3">
      <ul className="flex flex-col divide-y divide-border rounded-lg border border-border bg-card">
        {days.map((d) => {
          const invalid = !d.isClosed && d.opensAt >= d.closesAt;
          return (
            <li key={d.day} className="flex flex-col gap-2 px-3 py-3 sm:flex-row sm:items-center sm:gap-4">
              <label className="flex min-h-11 items-center justify-between gap-3 sm:w-48">
                <span className="text-sm font-semibold text-foreground">{dayNames[d.day]}</span>
                <span className="flex items-center gap-2 text-sm text-muted-foreground">
                  {d.isClosed ? "Kapalı" : "Açık"}
                  <Switch
                    checked={!d.isClosed}
                    disabled={disabled}
                    onCheckedChange={(open) => update(d.day, { isClosed: !open })}
                    aria-label={`${dayNames[d.day]} açık`}
                  />
                </span>
              </label>
              {!d.isClosed && (
                <div className="flex flex-1 items-center gap-2">
                  <input
                    type="time"
                    step={300}
                    value={d.opensAt}
                    disabled={disabled}
                    aria-label={`${dayNames[d.day]} açılış`}
                    aria-invalid={invalid || undefined}
                    onChange={(e) => update(d.day, { opensAt: e.target.value })}
                    className={cn(timeClass, invalid && "border-danger-600")}
                  />
                  <span aria-hidden className="text-muted-foreground">
                    –
                  </span>
                  <input
                    type="time"
                    step={300}
                    value={d.closesAt}
                    disabled={disabled}
                    aria-label={`${dayNames[d.day]} kapanış`}
                    aria-invalid={invalid || undefined}
                    onChange={(e) => update(d.day, { closesAt: e.target.value })}
                    className={cn(timeClass, invalid && "border-danger-600")}
                  />
                </div>
              )}
            </li>
          );
        })}
      </ul>
      {first && !disabled && (
        <Button type="button" variant="outline" onClick={applyToAll} className="self-start">
          <CopyCheck aria-hidden className="size-4" />
          {dayNames[first.day]} saatlerini tüm açık günlere uygula
        </Button>
      )}
    </div>
  );
}

const timeClass =
  "h-11 min-w-0 flex-1 rounded-lg border border-input bg-background px-3 text-base text-foreground tabular-nums outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring disabled:bg-muted";
