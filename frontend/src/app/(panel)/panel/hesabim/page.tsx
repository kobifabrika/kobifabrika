import type { Metadata } from "next";

import { AccountSettings } from "./account-settings";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Hesabım" };

export default function AccountPage() {
  return <AccountSettings />;
}
