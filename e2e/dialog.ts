import { expect, type Locator, type Page } from "@playwright/test";

/**
 * Waits until every open dialog has finished opening.
 *
 * `sap.m.Dialog` moves focus to its first field when its opening animation
 * completes — `Dialog#_handleOpened` calls `_setInitialFocus`, roughly 230ms
 * after `open()`. Playwright drives the interface faster than that, so a test
 * that starts filling the dialog straight away is still typing when the focus
 * change lands.
 *
 * That is what made the item suggestion flake: the ComboBox's popover was open
 * and its entry clickable, then the dialog took focus back, the popover closed
 * on the focus loss, and the click found the entry present in the DOM but no
 * longer visible. Popover visibility was never the discriminator — the popover
 * died after the click was already waiting on it.
 *
 * Waiting for the popup to reach OPEN puts that focus change before the first
 * interaction rather than somewhere in the middle of it, which makes the race
 * impossible instead of merely unlikely.
 */
export async function waitForDialogOpen(page: Page): Promise<void> {
  await page.waitForFunction(() => {
    const registry = (
      window as unknown as {
        sap?: { ui?: { core?: { Element?: { registry?: { all(): object } } } } };
      }
    ).sap?.ui?.core?.Element?.registry;
    if (!registry) {
      return false;
    }
    const open = Object.values(registry.all()).filter(
      (control: any) => control.isA?.("sap.m.Dialog") && control.isOpen?.(),
    );
    return open.length > 0 && open.every((d: any) => d.oPopup?.getOpenState() === "OPEN");
  });
}

/**
 * Picks one option from a `sap.m.Select`, and does not return until the pick
 * has landed.
 *
 * The same race `waitForDialogOpen` guards, one control down — but the losing
 * side is the *close*, not the open. A Select toggles its picker on click.
 * Picking an option starts an animated close and hands focus back to the
 * field; a click that arrives before that finishes is swallowed, so the picker
 * never reopens and the next option sits in the DOM, never visible, until the
 * test times out waiting for it.
 *
 * `invoices.spec.ts` hit exactly that in CI, on the third of three consecutive
 * picks — the first two won the race and the third did not. A 4x CPU throttle,
 * standing in for CI's slower machine, reproduces it every run and pins the
 * swallowed click as the cause: with the throttle on, the second click leaves
 * the picker closed rather than merely slow to open.
 *
 * So this waits for the picker to be OPEN before clicking an option, and for
 * it to be gone and the value applied before returning — the same discipline
 * the ComboBox picks in these specs already follow.
 */
export async function pickSelectOption(
  page: Page,
  select: Locator,
  optionName: string,
): Promise<void> {
  await select.click();
  await waitForSelectPicker(page, "OPEN");

  await page.getByRole("option", { name: optionName }).click();

  // the picker is gone and the field shows the pick: only then is another
  // click on this Select safe
  await waitForSelectPicker(page, "CLOSED");
  await expect(select).toContainText(optionName);
}

/** Waits until every `sap.m.Select` picker has settled into the given state. */
async function waitForSelectPicker(page: Page, state: "OPEN" | "CLOSED"): Promise<void> {
  await page.waitForFunction((want) => {
    const registry = (
      window as unknown as {
        sap?: { ui?: { core?: { Element?: { registry?: { all(): object } } } } };
      }
    ).sap?.ui?.core?.Element?.registry;
    if (!registry) {
      return false;
    }
    const open = Object.values(registry.all()).filter(
      (control: any) => control.isA?.("sap.m.Select") && control.isOpen?.(),
    );
    return want === "CLOSED"
      ? open.length === 0
      : open.length > 0 &&
          open.every((s: any) => s.getPicker?.()?.oPopup?.getOpenState() === "OPEN");
  }, state);
}
