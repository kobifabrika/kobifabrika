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
  receiptWidth?: 58 | 80;
  /** Ek üyelikler (aktif işletme her zaman ilk sıradadır). */
  otherMemberships?: Me["memberships"];
  /** KobiFabrika ekibi (süper admin). */
  platformAdmin?: boolean;
  /** SMS sağlayıcısı açık mı (pilotta kapalı; varsayılan açık: eski SMS ekranlarının testleri). */
  smsEnabled?: boolean;
  /** Etkin paketin özellikleri (varsayılan: hepsi açık, pilot). */
  features?: string[];
};

export function testMe({
  role = "Owner",
  modules = { appointments: true, stock: true },
  emailConfirmed = true,
  businessName = "Şık Kuaför",
  logoUrl = null,
  receiptWidth = 80,
  otherMemberships = [],
  platformAdmin = false,
  smsEnabled = true,
  features = ["campaigns", "automations", "onlineBooking", "commissions", "surveys", "riskList"],
}: Options = {}): Me {
  return {
    user: {
      id: "u1",
      fullName: "Emir Kaya",
      email: "emir@test.com",
      emailConfirmed,
      phone: "+905321234567",
      isPlatformAdmin: platformAdmin,
    },
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
      defaultDueDays: 30,
      city: "İstanbul",
      district: "Kadıköy",
      receiptWidth,
      plan: {
        status: "Pilot",
        code: "usta",
        name: "Usta",
        trialEndsAt: null,
        isReadOnly: false,
        features,
        maxUsers: null,
        maxContacts: null,
        maxAppointmentsPerMonth: null,
        requestedPlan: null,
        requestedPeriod: null,
      },
    },
    role,
    platform: { smsEnabled },
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
