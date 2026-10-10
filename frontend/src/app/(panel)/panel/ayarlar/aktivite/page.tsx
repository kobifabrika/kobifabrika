import type { Metadata } from "next";

import { ActivityLog } from "./activity-log";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Aktivite günlüğü" };

export default function ActivityPage() {
  return <ActivityLog />;
}
