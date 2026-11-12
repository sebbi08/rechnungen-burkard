/**
 * Restoring a backup replaces the whole database. A page opened before that
 * still shows the old data, and a write from it would land on the new data.
 * The server numbers the data it holds (X-Data-Generation on every answer) and
 * refuses a write that names another number (409 DataReplaced).
 *
 * The first number seen on a successful answer is kept and never replaced by a
 * later one: during a swap a 503 still carries the old number, and after a restore a stale tab's
 * background request would carry the new one -- adopting it would let that
 * tab write again. A reload starts fresh, which is how a tab recovers.
 */
const HEADER = "X-Data-Generation";

let installed = false;
let generation: string | null = null;

function isOData(url: string): boolean {
  try {
    return new URL(url, window.location.href).pathname.startsWith("/odata");
  } catch {
    return false;
  }
}

export function installDataGenerationGuard(): void {
  if (installed) {
    return;
  }
  installed = true;
  const original = window.fetch.bind(window);
  window.fetch = async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
    const request = input instanceof Request ? input : null;
    const url = request?.url ?? (input instanceof URL ? input.href : (input as string));
    const method = (init?.method ?? request?.method ?? "GET").toUpperCase();
    const odata = isOData(url);
    let nextInit = init;
    let nextInput = input;
    if (odata && generation !== null && method !== "GET" && method !== "HEAD") {
      if (request && !init?.headers) {
        const headers = new Headers(request.headers);
        headers.set(HEADER, generation);
        nextInput = new Request(request, { headers });
      } else {
        const headers = new Headers(init?.headers ?? request?.headers);
        headers.set(HEADER, generation);
        nextInit = { ...init, headers };
      }
    }
    const response = await original(nextInput, nextInit);
    // only from an answer that succeeded: a tab opened during a swap would
    // otherwise keep the old number from its 503 and write into the restored data
    if (odata && generation === null && response.ok) {
      generation = response.headers.get(HEADER);
    }
    return response;
  };
}
