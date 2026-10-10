"use client";

import { Check, Circle } from "lucide-react";
import { cn } from "cn";
import { useState, type ComponentProps } from "react";

import { Input } from "@/components/ui/input";
import { passwordHints } from "@/lib/validation/rules";

/** Şifre kutusu + Göster/Gizle (44px dokunma alanı). */
export function PasswordInput({ className, ...props }: ComponentProps<"input">) {
  const [visible, setVisible] = useState(false);

  return (
    <div className="relative">
      <Input {...props} type={visible ? "text" : "password"} className={cn("pr-20", className)} />
      <button
        type="button"
        onClick={() => setVisible((v) => !v)}
        aria-pressed={visible}
        aria-label={visible ? "Şifreyi gizle" : "Şifreyi göster"}
        className="absolute inset-y-0 right-0 min-w-16 rounded-r-lg px-3 text-sm font-medium text-primary"
      >
        {visible ? "Gizle" : "Göster"}
      </button>
    </div>
  );
}

/** Yeni şifre için canlı kural listesi (backend ile aynı kurallar). */
export function PasswordHints({ value }: { value: string }) {
  return (
    <ul className="flex flex-col gap-1" aria-label="Şifre kuralları">
      {passwordHints.map(({ label, test }) => {
        const ok = test(value);
        return (
          <li key={label} className={cn("flex items-center gap-2 text-xs", ok ? "text-foreground" : "text-muted-foreground")}>
            {ok ? <Check aria-hidden className="size-4" /> : <Circle aria-hidden className="size-3" />}
            {label}
            <span className="sr-only">{ok ? " (tamam)" : " (eksik)"}</span>
          </li>
        );
      })}
    </ul>
  );
}
