"use client";

import { useQueryClient } from "@tanstack/react-query";
import { cn } from "cn";
import { Check } from "lucide-react";
import type { ReactNode } from "react";

import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { appointmentStatusNames, type AppointmentStatus } from "@/lib/api/types";
import { useIsDesktop } from "@/lib/use-media";

export const calendarKey = ["calendar"] as const;
export const appointmentsKey = ["appointments"] as const;

/** Randevu değişince takvim, Bugün, müşteri kartı ve satışlar yenilenir. */
export function useAppointmentRefresh() {
  const queryClient = useQueryClient();
  return () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: calendarKey }),
      queryClient.invalidateQueries({ queryKey: appointmentsKey }),
      queryClient.invalidateQueries({ queryKey: ["dashboard"] }),
      queryClient.invalidateQueries({ queryKey: ["contacts"] }),
    ]);
}

/**
 * Bloğun zemini durumu gösterir (yeşil/sarı yok; docs/04): Beklemede kesik çerçeve, Onaylı açık gri dolu, Tamamlandı beyaz + ✓,
 * Gelmedi üstü çizili, İptal soluk kesik çerçeve.
 */
export const statusBlockClass: Record<AppointmentStatus, string> = {
  Pending: "border border-dashed border-black bg-white text-black",
  Confirmed: "border border-neutral-300 bg-neutral-100 text-black",
  Completed: "border border-neutral-300 bg-white text-black",
  NoShow: "border border-neutral-300 bg-white text-neutral-700 line-through",
  Cancelled: "border border-dashed border-neutral-300 bg-white text-neutral-600",
};

export function StatusBadge({ status, className }: { status: AppointmentStatus; className?: string }) {
  return (
    <span
      className={cn(
        "inline-flex min-h-6 items-center gap-1 rounded-md px-2 text-xs font-semibold",
        status === "Pending" && "border border-dashed border-black text-black",
        status === "Confirmed" && "bg-neutral-100 text-black",
        status === "Completed" && "bg-black text-white",
        status === "NoShow" && "border border-black text-black",
        status === "Cancelled" && "border border-neutral-400 text-neutral-700",
        className,
      )}
    >
      {status === "Completed" && <Check aria-hidden className="size-3" />}
      {appointmentStatusNames[status]}
    </span>
  );
}

/** Masaüstünde ortada pencere, telefonda alttan açılan sayfa. */
export function ResponsivePanel({
  open,
  onOpenChange,
  title,
  description,
  children,
  wide = false,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description?: ReactNode;
  children: ReactNode;
  wide?: boolean;
}) {
  const desktop = useIsDesktop();
  if (desktop)
    return (
      <Dialog open={open} onOpenChange={onOpenChange}>
        <DialogContent className={cn("max-h-[90vh] overflow-y-auto", wide ? "sm:max-w-lg" : "sm:max-w-md")}>
          <DialogHeader>
            <DialogTitle>{title}</DialogTitle>
            {description ? <DialogDescription>{description}</DialogDescription> : <DialogDescription className="sr-only">{title}</DialogDescription>}
          </DialogHeader>
          {children}
        </DialogContent>
      </Dialog>
    );
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="bottom" className="max-h-[92vh] gap-0 overflow-y-auto rounded-t-xl pb-[calc(1rem+env(safe-area-inset-bottom))]">
        <SheetHeader>
          <SheetTitle>{title}</SheetTitle>
          {description ? <SheetDescription>{description}</SheetDescription> : <SheetDescription className="sr-only">{title}</SheetDescription>}
        </SheetHeader>
        <div className="flex flex-col gap-4 px-4">{children}</div>
      </SheetContent>
    </Sheet>
  );
}
