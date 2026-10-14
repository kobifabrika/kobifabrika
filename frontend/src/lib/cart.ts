import type { DocumentLineRequest, PaymentMethod, Product, ServicePackage } from "@/lib/api/types";
import { calculate, formatQuantity, round2, type CalcResult, type Discount, type Unit } from "@/lib/sales";

/** Sepet satırı. Fiyat KDV dahil; ürün satırında sunucuya açıkça gönderilir ki önizleme ile kayıt aynı olsun. */
export type CartLine = {
  key: string;
  productId: string | null;
  name: string;
  unit: Unit;
  unitPrice: number;
  vatRate: number;
  quantity: number;
  discount: Discount | null;
  trackStock: boolean;
  /** Ürün sepete eklendiğindeki stok (uyarı için). */
  stock: number | null;
  /** Paket satırı: adet 1, satışla müşteriye paket açılır (müşteri zorunlu). */
  packageId?: string | null;
  /** Ürün satırında ad değiştirildi (randevudan "Paket: …" satırı); açıklama sunucuya gönderilir. */
  customName?: boolean;
};

export type CartContact = { id: string; name: string; phone?: string | null };

export type Cart = { lines: CartLine[]; discount: Discount | null; contact: CartContact | null; note: string };

export const emptyCart: Cart = { lines: [], discount: null, contact: null, note: "" };

export type CartAction =
  | { type: "add"; product: Product }
  | { type: "addPackage"; pkg: ServicePackage }
  | { type: "addDraft"; product: Product; name: string; unitPrice: number }
  | { type: "addFree"; name: string; unitPrice: number; vatRate: number }
  | { type: "quantity"; key: string; quantity: number }
  | { type: "step"; key: string; delta: 1 | -1 }
  | { type: "update"; key: string; patch: Partial<Pick<CartLine, "quantity" | "unitPrice" | "discount">> }
  | { type: "remove"; key: string }
  | { type: "discount"; discount: Discount | null }
  | { type: "contact"; contact: CartContact | null }
  | { type: "note"; note: string }
  | { type: "load"; cart: Cart }
  | { type: "clear" };

let counter = 0;
const newKey = () => `l${Date.now().toString(36)}${(counter++).toString(36)}`;

export function lineFromProduct(product: Product): CartLine {
  return {
    key: newKey(),
    productId: product.id,
    name: product.name,
    unit: product.unit,
    unitPrice: product.grossSalePrice,
    vatRate: product.vatRate,
    quantity: 1,
    discount: null,
    trackStock: product.trackStock,
    stock: product.trackStock ? product.stockQuantity : null,
  };
}

/**
 * Sepet işlemleri. Aynı ürün tekrar eklenirse (barkod iki kez okutulursa) yeni satır açılmaz, adet 1 artar
 * (fiyatı değiştirilmiş veya indirimli satır hariç). Adet 0'a inerse satır silinir.
 */
export function cartReducer(cart: Cart, action: CartAction): Cart {
  switch (action.type) {
    case "add": {
      const same = cart.lines.find(
        (l) => l.productId === action.product.id && l.unitPrice === action.product.grossSalePrice && !l.discount,
      );
      if (same) return { ...cart, lines: cart.lines.map((l) => (l === same ? { ...l, quantity: round3(l.quantity + 1) } : l)) };
      return { ...cart, lines: [...cart.lines, lineFromProduct(action.product)] };
    }
    case "addPackage":
      return {
        ...cart,
        lines: [
          ...cart.lines,
          {
            key: newKey(),
            productId: null,
            packageId: action.pkg.id,
            name: action.pkg.name,
            unit: "Package",
            unitPrice: action.pkg.price,
            vatRate: action.pkg.vatRate,
            quantity: 1,
            discount: null,
            trackStock: false,
            stock: null,
          },
        ],
      };
    case "addDraft":
      return {
        ...cart,
        lines: [
          ...cart.lines,
          { ...lineFromProduct(action.product), name: action.name, unitPrice: action.unitPrice, customName: action.name !== action.product.name },
        ],
      };
    case "addFree":
      return {
        ...cart,
        lines: [
          ...cart.lines,
          {
            key: newKey(),
            productId: null,
            name: action.name,
            unit: "Piece",
            unitPrice: action.unitPrice,
            vatRate: action.vatRate,
            quantity: 1,
            discount: null,
            trackStock: false,
            stock: null,
          },
        ],
      };
    case "quantity":
      return {
        ...cart,
        lines:
          action.quantity <= 0
            ? cart.lines.filter((l) => l.key !== action.key)
            : cart.lines.map((l) => (l.key === action.key ? { ...l, quantity: action.quantity } : l)),
      };
    case "step": {
      const line = cart.lines.find((l) => l.key === action.key);
      if (!line || (line.packageId && action.delta > 0)) return cart;
      return cartReducer(cart, { type: "quantity", key: action.key, quantity: round3(line.quantity + action.delta) });
    }
    case "update":
      // Paket satırının adedi her zaman 1.
      return {
        ...cart,
        lines: cart.lines.map((l) => (l.key === action.key ? { ...l, ...action.patch, ...(l.packageId ? { quantity: 1 } : {}) } : l)),
      };
    case "remove":
      return { ...cart, lines: cart.lines.filter((l) => l.key !== action.key) };
    case "discount":
      return { ...cart, discount: action.discount };
    case "contact":
      return { ...cart, contact: action.contact };
    case "note":
      return { ...cart, note: action.note };
    case "load":
      return action.cart;
    case "clear":
      return emptyCart;
  }
}

const round3 = (v: number) => Math.round(v * 1000) / 1000;

export function cartTotals(cart: Pick<Cart, "lines" | "discount">): CalcResult {
  return calculate(
    cart.lines.map((l) => ({ quantity: l.quantity, unitPrice: l.unitPrice, vatRate: l.vatRate, discount: l.discount })),
    cart.discount,
  );
}

/** API'ye giden kalemler (fiyat açıkça gönderilir). */
export function toLineRequests(lines: CartLine[]): DocumentLineRequest[] {
  return lines.map((l) => ({
    productId: l.productId,
    description: l.productId && !l.customName ? null : l.name,
    quantity: l.quantity,
    unitPrice: l.unitPrice,
    vatRate: l.productId || l.packageId ? null : l.vatRate,
    discount: l.discount,
    servicePackageId: l.packageId ?? null,
  }));
}

/** Sepette paket varsa müşteri zorunlu (paket müşterinin kartına yazılır). */
export function packageWithoutContact(cart: Pick<Cart, "lines" | "contact">): boolean {
  return !cart.contact && cart.lines.some((l) => !!l.packageId);
}

/** Stok yetersizse satır altındaki uyarı; değilse null. */
export function stockWarning(line: CartLine): string | null {
  if (!line.trackStock || line.stock === null || line.quantity <= line.stock) return null;
  return line.stock <= 0
    ? "Stokta yok; satış stoğu eksiye düşürür."
    : `Stokta ${formatQuantity(line.stock, line.unit)} var; satış stoğu eksiye düşürür.`;
}

// Ödeme

export type PaymentDraft = { method: PaymentMethod; amount: number };

/** Ödemeler toplamından sonra kalan (eksi = fazla ödeme). */
export function remaining(total: number, payments: PaymentDraft[]): number {
  return round2(total - payments.reduce((sum, p) => sum + p.amount, 0));
}

/** Nakitte alınan paradan para üstü (yetersizse 0). */
export function changeDue(cashAmount: number, received: number | null): number {
  return received !== null && received > cashAmount ? round2(received - cashAmount) : 0;
}

/** Ödeme doğrulaması (sunucuyla aynı mesajlar). */
export function paymentError(total: number, payments: PaymentDraft[], hasContact: boolean): string | null {
  if (total === 0) return null;
  if (payments.length === 0) return "Ödeme tipini seçin.";
  if (payments.some((p) => !(p.amount > 0))) return "Her ödemenin tutarını girin.";
  const left = remaining(total, payments);
  if (left > 0) return `Ödemeler toplamı satış tutarından ${formatTl(left)} eksik.`;
  if (left < 0) return `Ödemeler toplamı satış tutarından ${formatTl(-left)} fazla.`;
  if (!hasContact && payments.some((p) => p.method === "Credit")) return "Veresiye için müşteri seçin.";
  return null;
}

/** Veresiye vadesi: bugün + işletmenin varsayılan vade günü (İstanbul günü, "YYYY-MM-DD"). */
export function defaultDueDate(today: string, days: number): string {
  const d = new Date(`${today}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

/** Vade satış gününden önce olamaz (sunucuyla aynı mesaj). */
export function dueDateError(dueDate: string, today: string): string | null {
  return dueDate && dueDate < today ? "Vade tarihi satış tarihinden önce olamaz." : null;
}

const tl = new Intl.NumberFormat("tr-TR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const formatTl = (v: number) => `${tl.format(v)} TL`;

// Taslak: sayfa yenilense de sepet kaybolmaz (sadece bu tarayıcıda).

export function loadDraft(key: string): Cart | null {
  try {
    const raw = window.localStorage.getItem(key);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as Cart;
    return Array.isArray(parsed.lines) ? { ...emptyCart, ...parsed } : null;
  } catch {
    return null;
  }
}

export function saveDraft(key: string, cart: Cart) {
  try {
    if (cart.lines.length === 0 && !cart.contact) window.localStorage.removeItem(key);
    else window.localStorage.setItem(key, JSON.stringify(cart));
  } catch {
    // Gizli pencere veya kapalı depolama: taslak tutulmaz, satış yine yapılabilir.
  }
}
