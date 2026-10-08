import { FetchClientError } from "@odata2ts/http-client-fetch";

/** A failed call, reduced to the backend's error code -- `{error:{code}}` --
 *  or `HTTP <status>` when the response carried none. The UI maps the code to
 *  a text (useErrorToast); client-side validation throws the same type with
 *  its own code (ItemRequired, InvalidVatRate, ...). */
export class ApiError extends Error {
  public constructor(
    public readonly code: string,
    public readonly status?: number,
    public readonly body?: unknown,
  ) {
    super(code);
    this.name = "ApiError";
  }
}

interface ErrorBody {
  error?: { code?: unknown };
}

function codeOf(body: unknown, status: number | undefined): string {
  const code = (body as ErrorBody | undefined)?.error?.code;
  return typeof code === "string" && code ? code : `HTTP ${status ?? "?"}`;
}

/** Anything thrown by a call -- our own fetches or the generated client. A
 *  call that got no answer at all is `NetworkError`: fetch rejects with the
 *  browser's own English sentence ("Failed to fetch"), which says nothing to
 *  the user and is not German. */
export function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) {
    return error;
  }
  if (error instanceof FetchClientError) {
    return error.status === undefined
      ? new ApiError("NetworkError")
      : new ApiError(codeOf(error.responseData, error.status), error.status, error.responseData);
  }
  if (error instanceof TypeError) {
    return new ApiError("NetworkError");
  }
  return new ApiError(error instanceof Error ? error.message : String(error));
}

/** A failure worth asking again about: no answer, or the server failing --
 *  a restart under the page answers 502 or 503 for a moment. */
export function isTransient(error: unknown): boolean {
  const { code, status } = toApiError(error);
  return code === "NetworkError" || (status !== undefined && status >= 500);
}

export async function request(url: string, init?: RequestInit): Promise<Response> {
  const response = await fetch(url, init);
  if (!response.ok) {
    const body: unknown = await response.json().catch(() => undefined);
    throw new ApiError(codeOf(body, response.status), response.status, body);
  }
  return response;
}

export async function getJson<T>(url: string): Promise<T> {
  return (await (await request(url)).json()) as T;
}

function jsonInit(payload: unknown): RequestInit {
  return {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  };
}

/** POSTs JSON. Answers the parsed body, or undefined for an empty one (201/204). */
export async function postJson<T = unknown>(url: string, payload: unknown): Promise<T> {
  const text = await (await request(url, jsonInit(payload))).text();
  return (text ? JSON.parse(text) : undefined) as T;
}

export async function getBlob(url: string): Promise<Blob> {
  return (await request(url)).blob();
}

export async function postBlob(url: string, payload: unknown): Promise<Blob> {
  return (await request(url, jsonInit(payload))).blob();
}
