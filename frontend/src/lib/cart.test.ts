import { describe, expect, it } from "vitest";

import type { Product, ServicePackage } from "@/lib/api/types";

import {
  cartReducer,
  cartTotals,
  changeDue,
  defaultDueDate,
  dueDateError,
  emptyCart,
  loadDraft,
  packageWithoutContact,
  paymentError,
  remaining,
  saveDraft,
  stockWarning,
  toLineRequests,
} from "./cart";

const product = (over: Partial<Product> = {}): Product => ({
  id: "p1",
  type: "Product",
  name: "Tişört",
  categoryId: null,
  categoryName: null,
  unit: "Piece",
  vatRate: 20,
  pricesIncludeVat: true,
  salePrice: 100,
  grossSalePrice: 100,
  purchasePrice: null,
  barcode: "111",
  durationMinutes: null,
  trackStock: true,
  criticalStockLevel: null,
  stockQuantity: 2,
  isLowStock: false,
  isActive: true,
  notes: null,
  createdAt: "2026-10-11T10:00:00Z",
  ...over,
});

describe("cartReducer", () => {
  it("increments quantity when the same product is scanned twice", () => {
    let cart = cartReducer(emptyCart, { type: "add", product: product() });
    cart = cartReducer(cart, { type: "add", product: product() });
    expect(cart.lines).toHaveLength(1);
    expect(cart.lines[0].quantity).toBe(2);
  });

  it("opens a new line when the existing one has a discount", () => {
    let cart = cartReducer(emptyCart, { type: "add", product: product() });
    cart = cartReducer(cart, { type: "update", key: cart.lines[0].key, patch: { discount: { type: "Percent", value: 10 } } });
    cart = cartReducer(cart, { type: "add", product: product() });
    expect(cart.lines).toHaveLength(2);
  });

  it("removes the line when quantity drops to zero", () => {
    let cart = cartReducer(emptyCart, { type: "add", product: product() });
    cart = cartReducer(cart, { type: "step", key: cart.lines[0].key, delta: -1 });
    expect(cart.lines).toHaveLength(0);
  });

  it("calculates totals with line and cart discounts", () => {
    let cart = cartReducer(emptyCart, { type: "add", product: product() });
    cart = cartReducer(cart, { type: "addFree", name: "Ütü", unitPrice: 30, vatRate: 10 });
    cart = cartReducer(cart, { type: "quantity", key: cart.lines[1].key, quantity: 3 });
    cart = cartReducer(cart, { type: "update", key: cart.lines[1].key, patch: { discount: { type: "Percent", value: 10 } } });
    cart = cartReducer(cart, { type: "discount", discount: { type: "Amount", value: 12.7 } });
    expect(cartTotals(cart).total).toBe(168.3);
    expect(toLineRequests(cart.lines)).toEqual([
      { productId: "p1", description: null, quantity: 1, unitPrice: 100, vatRate: null, discount: null, servicePackageId: null },
      { productId: null, description: "Ütü", quantity: 3, unitPrice: 30, vatRate: 10, discount: { type: "Percent", value: 10 }, servicePackageId: null },
    ]);
  });

  it("warns when quantity exceeds stock", () => {
    let cart = cartReducer(emptyCart, { type: "add", product: product() });
    expect(stockWarning(cart.lines[0])).toBeNull();
    cart = cartReducer(cart, { type: "quantity", key: cart.lines[0].key, quantity: 3 });
    expect(stockWarning(cart.lines[0])).toBe("Stokta 2 adet var; satış stoğu eksiye düşürür.");
  });
});

describe("payments", () => {
  it("computes remaining and change", () => {
    expect(remaining(250, [{ method: "Cash", amount: 100 }])).toBe(150);
    expect(changeDue(150, 200)).toBe(50);
    expect(changeDue(150, 100)).toBe(0);
  });

  it("validates like the server", () => {
    expect(paymentError(100, [{ method: "Cash", amount: 90 }], false)).toBe("Ödemeler toplamı satış tutarından 10,00 TL eksik.");
    expect(paymentError(100, [{ method: "Credit", amount: 100 }], false)).toBe("Veresiye için müşteri seçin.");
    expect(paymentError(100, [{ method: "Credit", amount: 60 }, { method: "Card", amount: 40 }], true)).toBeNull();
    expect(paymentError(0, [], false)).toBeNull();
  });
});

describe("veresiye vadesi", () => {
  it("varsayılan vade bugün + ayardaki gün; ay ve yıl sınırını geçer", () => {
    expect(defaultDueDate("2026-10-12", 30)).toBe("2026-11-11");
    expect(defaultDueDate("2026-12-15", 30)).toBe("2027-01-14");
    expect(defaultDueDate("2026-10-12", 0)).toBe("2026-10-12");
  });

  it("vade satış gününden önce olamaz (sunucuyla aynı mesaj)", () => {
    expect(dueDateError("2026-10-11", "2026-10-12")).toBe("Vade tarihi satış tarihinden önce olamaz.");
    expect(dueDateError("2026-10-12", "2026-10-12")).toBeNull();
    expect(dueDateError("", "2026-10-12")).toBeNull();
  });
});

describe("draft", () => {
  it("survives a reload and clears when empty", () => {
    const cart = cartReducer(emptyCart, { type: "add", product: product() });
    saveDraft("test-cart", cart);
    expect(loadDraft("test-cart")?.lines[0].name).toBe("Tişört");
    saveDraft("test-cart", emptyCart);
    expect(loadDraft("test-cart")).toBeNull();
  });
});

describe("paket satırı", () => {
  const pkg: ServicePackage = {
    id: "p1",
    name: "10 seans lazer",
    productId: "s1",
    serviceName: "Lazer",
    sessionCount: 10,
    price: 3000,
    vatRate: 20,
    validityDays: 90,
    isActive: true,
    soldCount: 0,
  };

  it("her paket ayrı satır, adet hep 1 ve müşteri zorunlu", () => {
    let cart = cartReducer(emptyCart, { type: "addPackage", pkg });
    cart = cartReducer(cart, { type: "addPackage", pkg });
    expect(cart.lines).toHaveLength(2);
    const key = cart.lines[0].key;
    cart = cartReducer(cart, { type: "step", key, delta: 1 });
    cart = cartReducer(cart, { type: "update", key, patch: { quantity: 3, unitPrice: 2500 } });
    expect(cart.lines[0]).toMatchObject({ quantity: 1, unitPrice: 2500, unit: "Package", packageId: "p1" });
    expect(packageWithoutContact(cart)).toBe(true);
    expect(packageWithoutContact({ ...cart, contact: { id: "c1", name: "Elif" } })).toBe(false);
    expect(toLineRequests(cart.lines)[0]).toEqual({
      productId: null,
      description: "10 seans lazer",
      quantity: 1,
      unitPrice: 2500,
      vatRate: null,
      discount: null,
      servicePackageId: "p1",
    });
  });

  it("randevudan paketli satış: ürün satırı 0 TL ve açıklamayla gider", () => {
    const cart = cartReducer(emptyCart, { type: "addDraft", product: product({ name: "Lazer" }), name: "Paket: 10 seans lazer (4/10)", unitPrice: 0 });
    const [line] = toLineRequests(cart.lines);
    expect(line).toMatchObject({ description: "Paket: 10 seans lazer (4/10)", unitPrice: 0, servicePackageId: null });
    const plain = cartReducer(emptyCart, { type: "addDraft", product: product({ name: "Lazer" }), name: "Lazer", unitPrice: 500 });
    expect(toLineRequests(plain.lines)[0].description).toBeNull();
  });
});
