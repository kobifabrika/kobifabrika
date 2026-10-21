"use client";

import * as React from "react";

import { Input } from "@/components/ui/input";
import { formatNumberText, formatNumberValue, type NumberKind } from "@/lib/number-input";

type Props = Omit<React.ComponentProps<"input">, "type" | "inputMode"> & {
  /** "decimal": rakam, nokta (binlik) ve tek virgül (para, miktar, yüzde, saat); "integer": sadece rakam (gün, adet, dakika). */
  kind?: NumberKind;
  allowNegative?: boolean;
  /** Odaktan çıkınca bu kadar ondalıkla yazılır (para alanları: 2 → "32.500,00"). */
  fixed?: number;
};

const setNativeValue = (input: HTMLInputElement, value: string) =>
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input, value);

/** İmlecin solundaki rakam ve virgül sayısı; biçimlendirmeden sonra imleç aynı rakamın yanına konur. */
const significant = (s: string) => s.replace(/[^\d,]/g, "").length;

/**
 * Sayı alanı (`lib/number-input.ts` kuralı): harf girilemez, binlikler yazarken noktayla gruplanır, telefonda sayısal klavye açılır;
 * odaktan çıkınca değer netleşir ("12.5" → "12,5", para alanında "32.500" → "32.500,00").
 * Kontrollü (`value` + `onChange`) ve react-hook-form `register` ile çalışır: değer, `onChange` çağrılmadan önce biçimlenir.
 * Eksi girilebilen alanda tam klavye açılır (iOS sayısal klavyede eksi yok).
 */
export function NumberInput({ kind = "decimal", allowNegative = false, fixed, onChange, onBlur, ...props }: Props) {
  const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const input = e.target;
    const next = formatNumberText(input.value, { kind, allowNegative });
    if (next !== input.value) {
      const before = significant(input.value.slice(0, input.selectionStart ?? input.value.length));
      setNativeValue(input, next);
      let caret = 0;
      for (let seen = 0; caret < next.length && seen < before; caret++) if (/[\d,]/.test(next[caret]!)) seen++;
      if (document.activeElement === input) input.setSelectionRange(caret, caret);
    }
    onChange?.(e);
  };

  const handleBlur = (e: React.FocusEvent<HTMLInputElement>) => {
    const input = e.target;
    const next = formatNumberValue(input.value, { kind, fixed });
    if (next !== input.value) {
      // React'in onChange'i tetiklensin (kontrollü alan ve react-hook-form yeni değeri görsün).
      setNativeValue(input, next);
      input.dispatchEvent(new Event("input", { bubbles: true }));
    }
    onBlur?.(e);
  };

  return (
    <Input
      type="text"
      inputMode={allowNegative ? "text" : kind === "integer" ? "numeric" : "decimal"}
      autoComplete="off"
      onChange={handleChange}
      onBlur={handleBlur}
      {...props}
    />
  );
}
