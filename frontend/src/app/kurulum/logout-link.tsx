"use client";

import { useLogout } from "@/components/providers/session-provider";

export function LogoutLink() {
  const logout = useLogout();
  return (
    <button type="button" onClick={() => logout()} className="min-h-11 px-2 text-sm font-semibold text-brand-500">
      Çıkış yap
    </button>
  );
}
