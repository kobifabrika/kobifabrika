"use client";

import { useState } from "react";

import { Field } from "@/components/forms/field";
import { Input } from "@/components/ui/input";
import { type DateRange, type RangePreset, presetRange, rangePresets } from "@/lib/date-ranges";
import { formatDayShort } from "@/lib/format";
import { Chip, ScrollRow } from "@/components/ui/scroll-row";

export type RangeValue = { preset: RangePreset; from?: string; to?: string };

export function resolveRange(value: RangeValue): DateRange {
  return presetRange(value.preset, undefined, { from: value.from, to: value.to });
}

/**
 * Tarih aralığı seçici: bugün, bu hafta, bu ay, geçen ay, bu yıl, özel aralık. Düğmeler telefonda alt satıra kayar
 * (yatay taşma yok); özel aralıkta iki tarih alanı.
 */
export function RangePicker({
  value,
  onChange,
  presets = rangePresets.map((p) => p.key),
  label = "Tarih aralığı",
}: {
  value: RangeValue;
  onChange: (v: RangeValue) => void;
  presets?: RangePreset[];
  label?: string;
}) {
  const range = resolveRange(value);
  const [from, setFrom] = useState(value.from ?? range.from);
  const [to, setTo] = useState(value.to ?? range.to);

  return (
    <div className="flex flex-col gap-3">
      <ScrollRow role="group" aria-label={label}>
        {rangePresets
          .filter((p) => presets.includes(p.key))
          .map((p) => (
            <Chip
              key={p.key}
              active={value.preset === p.key}
              onClick={() => onChange(p.key === "ozel" ? { preset: "ozel", from, to } : { preset: p.key })}
            >
              {p.label}
            </Chip>
          ))}
      </ScrollRow>
      {value.preset === "ozel" ? (
        <div className="grid max-w-md grid-cols-2 gap-3">
          <Field label="Başlangıç">
            <Input
              type="date"
              value={from}
              onChange={(e) => {
                setFrom(e.target.value);
                if (e.target.value) onChange({ preset: "ozel", from: e.target.value, to });
              }}
            />
          </Field>
          <Field label="Bitiş">
            <Input
              type="date"
              value={to}
              onChange={(e) => {
                setTo(e.target.value);
                if (e.target.value) onChange({ preset: "ozel", from, to: e.target.value });
              }}
            />
          </Field>
        </div>
      ) : (
        <p className="text-sm text-neutral-600 tabular-nums">
          {range.from === range.to ? formatDayShort(range.from) : `${formatDayShort(range.from)} – ${formatDayShort(range.to)}`}
        </p>
      )}
    </div>
  );
}
