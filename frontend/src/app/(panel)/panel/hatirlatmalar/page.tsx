import type { Metadata } from "next";

import { ReminderList } from "@/components/contacts/reminders";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Hatırlatmalar" };

export default function RemindersPage() {
  return (
    <div className="mx-auto flex max-w-4xl flex-col gap-6">
      <h1 className="text-2xl font-bold tracking-tight text-black">Hatırlatmalar</h1>
      <ReminderList />
    </div>
  );
}
