import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { presetFrom } from "@/components/marketing/campaign-wizard";
import { MarketingCards, emptySlotCampaignHref } from "@/components/marketing/suggestions";
import { renderWithSession } from "@/test/session";

const empty = { emptySlots: { date: "2026-10-16", items: [] }, atRisk: { count: 0, items: [] } };

describe("Bugün pazarlama kartları", () => {
  it("öneri yoksa kart göstermez", () => {
    const { container } = renderWithSession(<MarketingCards data={empty} />);
    expect(container).toBeEmptyDOMElement();
  });

  it("boş saat ve risk kartları kampanyaya bağlanır", () => {
    renderWithSession(
      <MarketingCards
        data={{
          emptySlots: {
            date: "2026-10-16",
            items: [{ staffId: "s1", staffName: "Ayşe Usta", start: "14:00", end: "16:00", minutes: 120 }],
          },
          atRisk: {
            count: 2,
            items: [
              {
                id: "c1",
                name: "Zeynep Kaya",
                lastVisitAt: "2026-09-01T10:00:00Z",
                daysSince: 45,
                avgIntervalDays: 14,
                visitCount: 5,
              },
            ],
          },
        }}
      />,
    );
    expect(screen.getByText("14:00-16:00")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "14:00-16:00 Ayşe Usta: kampanya başlat" })).toHaveAttribute(
      "href",
      emptySlotCampaignHref("2026-10-16", { start: "14:00", end: "16:00", staffName: "Ayşe Usta" }),
    );
    expect(screen.getByText("2 düzenli müşteri her zamankinden uzun süredir gelmedi.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Kampanya başlat/ })).toHaveAttribute(
      "href",
      "/panel/kampanyalar/yeni?segment=at-risk&tur=risk",
    );
  });

  it("SMS kapalıyken (pilot) öneriler görünür ama kampanya düğmesi yok", () => {
    renderWithSession(
      <MarketingCards
        data={{
          emptySlots: {
            date: "2026-10-16",
            items: [{ staffId: "s1", staffName: "Ayşe Usta", start: "14:00", end: "16:00", minutes: 120 }],
          },
          atRisk: { count: 0, items: [] },
        }}
      />,
      { smsEnabled: false },
    );
    expect(screen.getByText("14:00-16:00")).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /kampanya/i })).not.toBeInTheDocument();
  });
});

describe("sihirbaz hazır mesajları", () => {
  it("boş saatten", () => {
    const p = presetFrom(
      new URLSearchParams(
        emptySlotCampaignHref("2026-10-16", { start: "14:00", end: "16:00", staffName: "Ayşe" }).split("?")[1],
      ),
    );
    expect(p).toMatchObject({ kind: "EmptySlot", name: "Boş saat 16 Ekim 14:00" });
    expect(p!.body).toBe("Merhaba {ad}, yarın 14:00-16:00 arası {isletme}'de yerimiz var. Randevu için bize yazın.");
  });

  it("risk listesinden ve hazır mesaj yoksa", () => {
    expect(presetFrom(new URLSearchParams("segment=at-risk&tur=risk"))).toMatchObject({ kind: "RiskList" });
    expect(presetFrom(new URLSearchParams("segment=debtors"))).toBeNull();
  });
});
