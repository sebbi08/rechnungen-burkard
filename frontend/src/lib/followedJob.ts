/** The half-year generation this browser started and has not yet shown the
 *  result of. A page reloaded mid-run picks it up again from here: the job's id
 *  otherwise lived only in the page that started it, which blocked nothing and
 *  reported nothing once reloaded (constitution VIII). Kept per browser on
 *  purpose -- someone else's run, on another device, is not this user's to
 *  wait for, and the server refuses a second run anyway. Storage can be
 *  unavailable (a private window, blocked site data); then there is simply
 *  nothing to pick up. */
const KEY = "followedGenerationJob";

export function rememberJob(jobId: number): void {
  try {
    localStorage.setItem(KEY, String(jobId));
  } catch {
    // nothing to pick up after a reload, as before
  }
}

export function forgetJob(): void {
  try {
    localStorage.removeItem(KEY);
  } catch {
    // nothing was kept
  }
}

export function rememberedJob(): number | undefined {
  try {
    const id = Number(localStorage.getItem(KEY));
    return Number.isInteger(id) && id > 0 ? id : undefined;
  } catch {
    return undefined;
  }
}
