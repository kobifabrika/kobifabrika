import { readFileSync } from "node:fs";
import path from "node:path";

import { describe, expect, it } from "vitest";

import { company, companyEmailFooter, companyLine } from "./company";
import { legalDocs } from "./legal";

describe("şirket bilgisi", () => {
  it("backend e-posta alt bilgisiyle aynı (Company:EmailFooter)", () => {
    const file = path.resolve(__dirname, "../../../backend/src/KobiFabrika.Api/appsettings.json");
    const settings = JSON.parse(readFileSync(file, "utf8")) as { Company: { EmailFooter: string } };
    expect(settings.Company.EmailFooter).toBe(companyEmailFooter);
  });

  it("boş adres ve vergi bilgisi künyede görünmez", () => {
    expect(companyLine()).toBe(company.name);
  });

  it("'kuruluş aşamasında' hiçbir metinde görünmez (karar 2026-10-21)", () => {
    const texts = [companyLine(), companyEmailFooter, JSON.stringify(legalDocs)];
    for (const text of texts) expect(text).not.toMatch(/kuruluş aşamas/i);
  });

  it("KVKK metninde veri sorumlusu bölümü dolu: ad ve iletişim adresi", () => {
    const section = legalDocs.kvkk.sections.find((s) => s.heading === "Veri sorumlusu");
    expect(section?.paragraphs?.[0]).toContain(company.name);
    expect(section?.paragraphs?.[0]).toContain(company.email);
  });
});
