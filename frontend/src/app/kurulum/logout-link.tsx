"use client";

import { useLogout } from "@/components/providers/session-provider";

export function LogoutLink() {
  const logout = useLogout();
  return (
    <button type="button" onClick={() => logout()} className="min-h-11 px-2 text-sm font-semibold text-black underline decoration-brand decoration-2 underline-offset-4 hover:decoration-black">
      Çıkış yap
    </button>
  );
}
