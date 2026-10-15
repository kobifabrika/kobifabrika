import { describe, expect, it } from "vitest";

import { can } from "./permissions";

describe("yetki matrisi (backend Policies.cs ile aynı)", () => {
  it.each([
    ["viewSettings", ["Owner", "Manager", "Accountant"]],
    ["manageBusiness", ["Owner", "Manager"]],
    ["manageTeam", ["Owner", "Manager"]],
    ["viewAuditLog", ["Owner", "Manager", "Accountant"]],
    ["manageCatalog", ["Owner", "Manager"]],
    ["viewPurchasePrice", ["Owner", "Manager", "Accountant"]],
    ["createSales", ["Owner", "Manager", "Staff"]],
    ["viewAllSales", ["Owner", "Manager", "Accountant"]],
    ["cancelSales", ["Owner", "Manager"]],
    ["manageCommissions", ["Owner", "Manager"]],
    ["viewAllCommissions", ["Owner", "Manager", "Accountant"]],
  ] as const)("%s → %j", (permission, allowed) => {
    for (const role of ["Owner", "Manager", "Staff", "Accountant"] as const) {
      expect(can(role, permission)).toBe((allowed as readonly string[]).includes(role));
    }
    expect(can(null, permission)).toBe(false);
  });
});
