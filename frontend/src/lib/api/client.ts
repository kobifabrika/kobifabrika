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
