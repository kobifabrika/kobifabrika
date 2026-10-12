"use client";

import { useSession } from "@/components/providers/session-provider";
import { greeting } from "@/lib/format";

/** Bugün ekranı başlığı: saate göre selamlama + kullanıcının adı, altında işletme adı. */
export function TodayGreeting() {
  const { me, business } = useSession();
  const firstName = me.user.fullName.trim().split(/\s+/)[0];

  return (
    <div className="min-w-0">
      <h1 className="text-2xl font-bold tracking-tight text-black">
        {greeting()}, {firstName}
      </h1>
      <p className="mt-1 truncate text-base font-semibold text-black" title={business.name}>
        {business.name}
      </p>
      <p className="mt-1 text-sm text-neutral-600">İşletmenizde bugün olanlar.</p>
    </div>
  );
}
