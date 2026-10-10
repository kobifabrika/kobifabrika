import { afterEach, describe, expect, it, vi } from "vitest";

import { ApiError, NetworkError, REQUEST_TIMEOUT_MS, errorMessage, fetchWithTimeout } from "./client";

describe("fetchWithTimeout", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it("API kapalıyken tarayıcının 'Failed to fetch' hatasını NetworkError'a çevirir", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("Failed to fetch")));

    const error = await fetchWithTimeout(new Request("http://localhost:5080/api/auth/me")).catch((e) => e);

    expect(error).toBeInstanceOf(NetworkError);
    expect(error.timedOut).toBe(false);
    expect(errorMessage(error)).toBe("Sunucuya ulaşılamıyor. İnternet bağlantınızı kontrol edip tekrar deneyin.");
  });

  it("yanıt gelmeyen isteği süre dolunca keser", async () => {
    vi.useFakeTimers();
    vi.stubGlobal(
      "fetch",
      vi.fn(
        (_: Request, init: RequestInit) =>
          new Promise((_resolve, reject) => init.signal?.addEventListener("abort", () => reject(init.signal?.reason))),
      ),
    );

    const pending = fetchWithTimeout(new Request("http://localhost:5080/api/contacts")).catch((e) => e);
    await vi.advanceTimersByTimeAsync(REQUEST_TIMEOUT_MS);
    const error = await pending;

    expect(error).toBeInstanceOf(NetworkError);
    expect(error.timedOut).toBe(true);
    expect(errorMessage(error)).toBe("Sunucu yanıt vermedi. Biraz sonra tekrar deneyin.");
  });

  it("çağıranın iptalini NetworkError'a çevirmez", async () => {
    const controller = new AbortController();
    vi.stubGlobal(
      "fetch",
      vi.fn(
        (_: Request, init: RequestInit) =>
          new Promise((_resolve, reject) => init.signal?.addEventListener("abort", () => reject(init.signal?.reason))),
      ),
    );

    const pending = fetchWithTimeout(new Request("http://localhost:5080/api/tags", { signal: controller.signal })).catch(
      (e) => e,
    );
    controller.abort();

    expect(await pending).not.toBeInstanceOf(NetworkError);
  });

  it("API'nin hata yanıtını olduğu gibi döner (ApiError'a unwrap çevirir)", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 500 })));

    const response = await fetchWithTimeout(new Request("http://localhost:5080/api/business"));

    expect(response.status).toBe(500);
    expect(errorMessage(new ApiError(500, { title: "Sunucu hatası" }))).toBe("Sunucu hatası");
  });
});
