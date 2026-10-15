"use client";

import { useSearchParams } from "next/navigation";

import { useSession } from "@/components/providers/session-provider";
import { useMyStaff } from "@/components/staff/staff-bits";
import { LeaveBalanceCard } from "@/components/staff/staff-leave-balance";
import { TabNav } from "@/components/ui/scroll-row";
import { can } from "@/lib/permissions";

import { LeaveCalendarView } from "./leave-calendar";
import { LeaveRequests } from "./leave-requests";
import { MyClock } from "./my-clock";
import { TimeEntriesDay } from "./time-entries";

/**
 * /panel/mesai-izin?sekme=mesaim|mesai|izinler|takvim. Personel: Mesaim (tek düğme), İzinlerim (talepler + bakiye), İzin takvimi.
 * Sahip/Yönetici: Mesai (gün görünümü), İzinler (onay), İzin takvimi; hesabı bir personele bağlıysa Mesaim de. Mali Müşavir salt okunur.
 */
export function AttendanceHome() {
  const { role } = useSession();
  const params = useSearchParams();
  const me = useMyStaff();
  const seeAll = can(role, "viewStaffTime");
  const linked = !!me.data?.staffMemberId;

  const tabs = [
    ...(!seeAll || linked ? [{ key: "mesaim", label: "Mesaim" }] : []),
    ...(seeAll ? [{ key: "mesai", label: "Mesai" }] : []),
    { key: "izinler", label: seeAll ? "İzinler" : "İzinlerim" },
    { key: "takvim", label: "İzin takvimi" },
  ];
  const fallback = seeAll ? "mesai" : "mesaim";
  const active = tabs.find((t) => t.key === params.get("sekme"))?.key ?? fallback;

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-2xl font-bold tracking-tight text-black">Mesai ve İzin</h1>
      <TabNav label="Mesai ve izin sekmeleri" active={active} tabs={tabs.map((t) => ({ ...t, href: `/panel/mesai-izin?sekme=${t.key}` }))} />
      {active === "mesaim" && <MyClock />}
      {active === "mesai" && <TimeEntriesDay />}
      {active === "izinler" && (
        <div className="flex flex-col gap-8">
          <LeaveRequests mine={!seeAll} />
          {!seeAll && me.data?.staffMemberId && (
            <section className="flex flex-col gap-3 border-t border-border pt-5">
              <h2 className="text-base font-semibold text-black">Yıllık izin bakiyem</h2>
              <LeaveBalanceCard staffId={me.data.staffMemberId} manage={false} />
            </section>
          )}
        </div>
      )}
      {active === "takvim" && <LeaveCalendarView />}
    </div>
  );
}
