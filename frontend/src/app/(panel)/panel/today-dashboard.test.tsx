import { screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { renderWithSession } from "@/test/session";

import { TodayDashboard } from "./today-dashboard";

const ok = (data: unknown) => ({ data, response: new Response(null, { status: 200 }) });

const days = Array.from({ length: 30 }, (_, i) => ({ date: `2026-10-${String(i + 1).padStart(2, "0")}`, total: i * 10, count: 1 }));

const base = {
  today: "2026-10-30",
  sales: { total: 1250, count: 3, byMethod: [{ method: "Cash", amount: 1250 }], onlyOwn: false },
  stock: { enabled: true, low: 1, negative: 0, items: [{ id: "p1", name: "Defter", quantity: 2, unit: "Piece", criticalLevel: 5 }] },
  recentSales: [],
  last30Days: days,
};

const full = {
  ...base,
  collections: 300,
  accounts: [{ id: "a1", name: "Nakit Kasa", type: "Cash", balance: -50 }],
  receivables: { overdue: 1000, overdueContacts: 2, dueToday: 400, dueTodayContacts: 1 },
};

const staffView = { ...base, sales: { ...base.sales, onlyOwn: true }, collections: null, accounts: null, receivables: null };

let response: unknown = full;

vi.mock("@/lib/api/client", async (original) => ({
  ...(await original<typeof import("@/lib/api/client")>()),
  api: { GET: () => Promise.resolve(ok(response)) },
}));

describe("Bugün ekranı", () => {
  it("Sahip satış, tahsilat, alacak, kritik stok ve bakiyeleri görür; Tahsilat al pencere açar", async () => {
    response = full;
    renderWithSession(<TodayDashboard />);

    expect(await screen.findByText("Bugünkü tahsilat")).toBeInTheDocument();
    expect(screen.getByText("300,00 TL")).toBeInTheDocument();
    expect(screen.getByText("Vadesi gelen alacak")).toBeInTheDocument();
    expect(screen.getByText("1.400,00 TL", { selector: "p" })).toBeInTheDocument();
    expect(screen.getByText("Eksi bakiye")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Vadesi gelen alacak/ })).toHaveAttribute("href", "/panel/cari-kasa?sekme=vadeler");
    expect(screen.getByRole("link", { name: /Satış yap/ })).toHaveAttribute("href", "/panel/satislar/yeni");
    // Tahsilat al vadeler sekmesine gitmez, müşteri seçicili pencereyi açar.
    expect(screen.getByRole("button", { name: /Tahsilat al/ })).toBeInTheDocument();
    expect(screen.queryByText("Bugünün randevuları")).not.toBeInTheDocument();
  });

  it("Personel sadece kendi satışlarını ve kritik stoğu görür", async () => {
    response = staffView;
    renderWithSession(<TodayDashboard />, { role: "Staff" });

    expect(await screen.findByText("Bugünkü satışlarım")).toBeInTheDocument();
    expect(screen.getAllByText("Kritik stok").length).toBeGreaterThan(0);
    expect(screen.getByText("Defter")).toBeInTheDocument();
    expect(screen.queryByText("Bugünkü tahsilat")).not.toBeInTheDocument();
    expect(screen.queryByText("Vadesi gelen alacak")).not.toBeInTheDocument();
    expect(screen.queryByText("Kasa ve banka")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Tahsilat al/ })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Satış yap/ })).toBeInTheDocument();
  });

  it("Mali Müşavir her şeyi görür ama hızlı işlem yoktur", async () => {
    response = full;
    renderWithSession(<TodayDashboard />, { role: "Accountant" });

    expect(await screen.findByText("Bugünkü tahsilat")).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Hızlı işlemler" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /Gider ekle/ })).not.toBeInTheDocument();
  });

  it("Randevular modülü açıksa bugünün randevuları en üstte; boşsa Randevu ver", async () => {
    response = {
      ...full,
      appointments: {
        total: 2,
        pending: 1,
        onlyOwn: false,
        items: [
          { id: "a1", startAt: "2026-10-30T07:00:00Z", endAt: "2026-10-30T07:45:00Z", contactName: "Ayşe Yılmaz", serviceName: "Saç kesimi", staffName: "Zeynep", status: "Confirmed" },
          { id: "a2", startAt: "2026-10-30T11:30:00Z", endAt: "2026-10-30T12:00:00Z", contactName: "Can Demir", serviceName: "Sakal", staffName: "Mert", status: "Pending" },
        ],
      },
    };
    renderWithSession(<TodayDashboard />);

    expect(await screen.findByText("Bugünün randevuları")).toBeInTheDocument();
    expect(screen.getByText("10:00")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Ayşe Yılmaz/ })).toHaveAttribute("href", "/panel/randevular?randevu=a1");
    expect(screen.getByText("Onay bekleyen 1")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Randevu ver/ })).toHaveAttribute("href", "/panel/randevular?yeni=1");
  });

  it("Personel kendi randevularını görür; randevu yoksa kart yine görünür", async () => {
    response = { ...staffView, appointments: { total: 0, pending: 0, onlyOwn: true, items: [] } };
    renderWithSession(<TodayDashboard />, { role: "Staff" });

    expect(await screen.findByText("Bugünkü randevularım")).toBeInTheDocument();
    expect(screen.getByText("Bugün randevu yok.")).toBeInTheDocument();
  });
});
