import type { Breadcrumb, ErrorEvent } from "@sentry/nextjs";

// Sentry'ye kişisel veri gitmez (backend SentryScrubber ile aynı kural): e-postanın yerel kısmı ve telefonun son iki hanesi
// dışı maskelenir; kullanıcı, IP, çerez ve başlıklar silinir.
const emailPattern = /[A-Za-z0-9._%+-]+@([A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,})/g;
const phonePattern =
  /(?<![\d+])(?:(?:\+?90[\s-]?)?0?5\d{2}[\s-]?\d{3}[\s-]?\d{2}[\s-]?(\d{2})|\+\d{8,12}(\d{2}))(?!\d)/g;

export function maskPersonalData<T extends string | undefined | null>(text: T): T {
  if (!text) return text;
  return text
    .replace(emailPattern, (_, domain: string) => `***@${domain}`)
    .replace(phonePattern, (_, a?: string, b?: string) => `[telefon **${a ?? b}]`) as T;
}

function maskValues(record: Record<string, unknown> | undefined) {
  if (!record) return record;
  return Object.fromEntries(
    Object.entries(record).map(([key, value]) => [key, typeof value === "string" ? maskPersonalData(value) : value]),
  );
}

export function scrubEvent(event: ErrorEvent): ErrorEvent {
  delete event.user;
  if (event.message) event.message = maskPersonalData(event.message);
  for (const exception of event.exception?.values ?? []) {
    exception.value = maskPersonalData(exception.value);
  }
  if (event.request) {
    delete event.request.cookies;
    delete event.request.headers;
    delete event.request.data;
    event.request.url = maskPersonalData(event.request.url);
    if (typeof event.request.query_string === "string") {
      event.request.query_string = maskPersonalData(event.request.query_string);
    }
  }
  event.extra = maskValues(event.extra);
  event.breadcrumbs = event.breadcrumbs?.map(scrubBreadcrumb);
  return event;
}

export function scrubBreadcrumb(breadcrumb: Breadcrumb): Breadcrumb {
  return { ...breadcrumb, message: maskPersonalData(breadcrumb.message), data: maskValues(breadcrumb.data) };
}
