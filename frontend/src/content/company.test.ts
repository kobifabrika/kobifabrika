import { readFileSync } from "node:fs";
import path from "node:path";

import { describe, expect, it } from "vitest";

import { company, companyEmailFooter, companyLine } from "./company";

describe("şirket bilgisi", () => {
  it("backend e-posta alt bilgisiyle aynı (Company:EmailFooter)", () => {
    const file = path.resolve(__dirname, "../../../backend/src/KobiFabrika.Api/appsettings.json");
    const settings = JSON.parse(readFileSync(file, "utf8")) as { Company: { EmailFooter: string } };
    expect(settings.Company.EmailFooter).toBe(companyEmailFooter);
  });

  it("boş adres ve vergi bilgisi künyede görünmez", () => {
    expect(companyLine()).toBe(company.name);
  });
});
