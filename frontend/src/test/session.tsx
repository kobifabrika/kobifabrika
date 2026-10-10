import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render } from "@testing-library/react";
import type { ReactElement } from "react";

import { SessionProvider } from "@/components/providers/session-provider";
import type { Me, Role } from "@/lib/api/types";

type Options = { role?: Role; modules?: { appointments: boolean; stock: boolean }; emailConfirmed?: boolean };

export function testMe({ role = "Owner", modules = { appointments: true, stock: true }, emailConfirmed = true }: Options = {}): Me {
  return {
    user: { id: "u1", fullName: "Emir Kaya", email: "emir@test.com", emailConfirmed, phone: "+905321234567" },
    business: {
      id: "b1",
      name: "Şık Kuaför",
      slug: "sik-kuafor",
      sector: "HairBarber",
      template: "Service",
      modules,
      logoUrl: null,
      setupCompleted: true,
    },
    role,
    memberships: [{ businessId: "b1", businessName: "Şık Kuaför", role }],
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
