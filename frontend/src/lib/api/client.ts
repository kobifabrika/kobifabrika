import createClient from "openapi-fetch";

import type { paths } from "./schema";

export const API_URL = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5080";

/**
 * Tipli API istemcisi. Tipler `npm run gen:api` ile backend'in OpenAPI dokümanından üretilir.
 * Kimlik doğrulama httpOnly cookie ile; bu yüzden her istek `credentials: "include"`.
 */
export const api = createClient<paths>({
  baseUrl: API_URL,
  credentials: "include",
});

/** RFC 9457 ProblemDetails: backend'in tüm hata yanıtları bu biçimde ve Türkçe. */
export type ProblemDetails = {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  correlationId?: string;
  errors?: Record<string, string[]>;
};

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly problem: ProblemDetails,
  ) {
    super(problem.detail ?? problem.title ?? "Beklenmeyen bir hata oluştu.");
    this.name = "ApiError";
  }
}

/** openapi-fetch sonucunu açar: hata varsa ApiError fırlatır, yoksa veriyi döner. */
export async function unwrap<T>(
  request: Promise<{ data?: T; error?: unknown; response: Response }>,
): Promise<T> {
  const { data, error, response } = await request;
  if (!response.ok || error !== undefined) {
    const problem = (error ?? {}) as ProblemDetails;
    throw new ApiError(response.status, problem);
  }
  return data as T;
}

/** Alan bazlı doğrulama hataları (ilk mesaj); form alanlarına bağlamak için. */
export function fieldErrors(error: unknown): Record<string, string> {
  if (!(error instanceof ApiError) || !error.problem.errors) return {};
  return Object.fromEntries(
    Object.entries(error.problem.errors)
      .filter(([, messages]) => messages.length > 0)
      .map(([field, messages]) => [field, messages[0]]),
  );
}

/** Kullanıcıya gösterilecek tek satırlık Türkçe hata. */
export function errorMessage(error: unknown, fallback = "Bir şeyler ters gitti. Lütfen tekrar deneyin."): string {
  if (error instanceof ApiError) {
    if (error.status === 429) return "Çok fazla deneme yaptınız. Biraz bekleyip tekrar deneyin.";
    const first = Object.values(fieldErrors(error))[0];
    return error.problem.detail ?? first ?? error.problem.title ?? fallback;
  }
  if (error instanceof TypeError) return "Sunucuya ulaşılamıyor. İnternet bağlantınızı kontrol edin.";
  return fallback;
}

/** Hata kodu (ör. "setup_required", "email_not_confirmed"); ProblemDetails `code` alanı. */
export function problemCode(error: unknown): string | undefined {
  return error instanceof ApiError ? (error.problem as { code?: string }).code : undefined;
}

/** API'nin döndürdüğü göreli dosya yolunu (/files/...) tam URL'ye çevirir. */
export function fileUrl(path: string | null | undefined): string | null {
  return path ? `${API_URL}${path}` : null;
}
