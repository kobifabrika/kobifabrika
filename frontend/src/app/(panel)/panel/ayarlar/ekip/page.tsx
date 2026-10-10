import type { Metadata } from "next";

import { TeamSettings } from "./team-settings";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Ekip" };

export default function TeamPage() {
  return <TeamSettings />;
}
