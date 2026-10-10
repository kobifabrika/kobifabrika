import { cn } from "cn";
import { cloneElement, useId, type ReactElement, type ReactNode } from "react";

import { Label } from "@/components/ui/label";

type FieldProps = {
  label: string;
  error?: string;
  hint?: ReactNode;
  className?: string;
  /** Tek bir form kontrolü (Input, select…); id, aria-invalid ve aria-describedby otomatik verilir. */
  children: ReactElement<{ id?: string; "aria-invalid"?: boolean; "aria-describedby"?: string }>;
};

/** Etiket + kontrol + hata/ipucu. Hata mesajı ekran okuyucuya kontrolle birlikte okunur. */
export function Field({ label, error, hint, className, children }: FieldProps) {
  const id = useId();
  const describedBy = [error && `${id}-error`, hint && `${id}-hint`].filter(Boolean).join(" ") || undefined;

  return (
    <div className={cn("flex flex-col gap-2", className)}>
      <Label htmlFor={id} className="text-sm font-semibold text-brand-900">
        {label}
      </Label>
      {cloneElement(children, { id, "aria-invalid": error ? true : undefined, "aria-describedby": describedBy })}
      {hint && !error && (
        <div id={`${id}-hint`} className="text-xs text-neutral-600">
          {hint}
        </div>
      )}
      {error && (
        <p id={`${id}-error`} role="alert" className="text-sm text-danger-600">
          {error}
        </p>
      )}
    </div>
  );
}

/** Form üstünde/altında genel hata veya bilgi kutusu. */
export function FormMessage({ tone = "error", children }: { tone?: "error" | "success" | "info"; children: ReactNode }) {
  return (
    <div
      role={tone === "error" ? "alert" : "status"}
      className={cn(
        "rounded-md px-3 py-2 text-sm",
        tone === "error" && "bg-danger-50 text-danger-600",
        tone === "success" && "bg-success-50 text-success-600",
        tone === "info" && "bg-neutral-100 text-brand-700",
      )}
    >
      {children}
    </div>
  );
}
