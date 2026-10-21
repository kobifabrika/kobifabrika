"use client";

import * as React from "react";

import { Input } from "@/components/ui/input";
import { normalizeNumberText, type NumberKind } from "@/lib/number-input";

type Props = Omit<React.ComponentProps<"input">, "type" | "inputMode"> & {
  /** "decimal": rakam ve tek virgül (para, miktar, yüzde, saat); "integer": sadece rakam (gün, adet, dakika). */
  kind?: NumberKind;
  allowNegative?: boolean;
};

/**
 * Sayı alanı: harf girilemez, nokta virgüle çevrilir (`normalizeNumberText`), telefonda sayısal klavye açılır.
 * Kontrollü (`value` + `onChange`) ve react-hook-form `register` ile çalışır: değer, `onChange` çağrılmadan önce süzülür.
 * Eksi girilebilen alanda tam klavye açılır (iOS sayısal klavyede eksi yok).
 */
export function NumberInput({ kind = "decimal", allowNegative = false, onChange, ...props }: Props) {
  const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const input = e.target;
    const native = e.nativeEvent as InputEvent;
    const typed = native.inputType === "insertText" && (native.data?.length ?? 0) === 1;
    const options = { kind, allowNegative, typed };
    const next = normalizeNumberText(input.value, options);
    if (next !== input.value) {
      // İmleç yerinde kalsın: imlecin solundaki metni de aynı kuralla süz.
      const caret = input.selectionStart ?? input.value.length;
      const before = normalizeNumberText(input.value.slice(0, caret), options).length;
      input.value = next;
      input.setSelectionRange(before, before);
    }
    onChange?.(e);
  };

  return (
    <Input
      type="text"
      inputMode={allowNegative ? "text" : kind === "integer" ? "numeric" : "decimal"}
      autoComplete="off"
      onChange={handleChange}
      {...props}
    />
  );
}
