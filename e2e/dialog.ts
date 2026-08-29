import type { Page } from "@playwright/test";

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
    return (
      open.length > 0 &&
      open.every((d: any) => d.oPopup?.getOpenState() === "OPEN")
    );
  });
}
