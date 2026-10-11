import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render } from "@testing-library/react";
import type { ReactElement } from "react";

import { SessionProvider } from "@/components/providers/session-provider";
import type { Me, Role } from "@/lib/api/types";

type Options = {
  role?: Role;
  modules?: { appointments: boolean; stock: boolean };
  emailConfirmed?: boolean;
  businessName?: string;
  logoUrl?: string | null;
  /** Ek üyelikler (aktif işletme her zaman ilk sıradadır). */
  otherMemberships?: Me["memberships"];
};

export function testMe({
  role = "Owner",
  modules = { appointments: true, stock: true },
  emailConfirmed = true,
  businessName = "Şık Kuaför",
  logoUrl = null,
  otherMemberships = [],
}: Options = {}): Me {
  return {
    user: { id: "u1", fullName: "Emir Kaya", email: "emir@test.com", emailConfirmed, phone: "+905321234567" },
    business: {
      id: "b1",
      name: businessName,
      slug: "sik-kuafor",
      sector: "HairBarber",
      template: "Service",
      modules,
      logoUrl,
      setupCompleted: true,
      phone: "+902125554433",
      address: "Moda Cad. No: 5",
      city: "İstanbul",
      district: "Kadıköy",
    },
    role,
    memberships: [{ businessId: "b1", businessName, role }, ...otherMemberships],
  };
}

/** Bileşeni panel oturumu ve TanStack Query ile render eder. */
export function renderWithSession(ui: ReactElement, options: Options = {}) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <SessionProvider me={testMe(options)}>{ui}</SessionProvider>
    </QueryClientProvider>,
  );
}
