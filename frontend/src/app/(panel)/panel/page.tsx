import type { Metadata } from "next";

import { TodayDashboard } from "./today-dashboard";
import { TodayGreeting } from "./today-greeting";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Bugün" };

/** Bugün ekranı: bugünkü satış ve tahsilat, bakiyeler, vadesi gelen alacaklar, kritik stok, son satışlar, 30 günlük grafik. */
export default function TodayPage() {
  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-6">
      <TodayGreeting />
      <TodayDashboard />
    </div>
  );
}
