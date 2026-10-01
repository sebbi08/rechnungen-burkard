import { expect, type Locator, type Page } from "@playwright/test";

/**
 * Helpers for the Vue frontend's specs (tests/vue). The UI5 counterparts are in
 * dialog.ts; the mechanism they guard against is the same, only the markers
 * differ.
 *
 * PrimeVue's Dialog moves focus to its `autofocus` field in `onAfterEnter`,
 * once its opening transition has finished — about 150ms after it appears. A
 * spec that starts typing straight away is still typing when that focus change
 * lands, and a Select it has opened in the meantime closes underneath it. Vue's
 * <Transition> marks an element that is still entering with `<name>-enter-active`
 * and removes the class in the same tick it calls `onAfterEnter`, so waiting for
 * the class to go puts the focus change before the first interaction.
 */
export async function waitForOverlayOpen(page: Page): Promise<void> {
  await page.waitForFunction(() => {
    const open = [...document.querySelectorAll(".p-dialog, .p-drawer")];
    return (
      open.length > 0 &&
      open.every(
        (element) =>
          !element.classList.contains("p-dialog-enter-active") &&
          !element.classList.contains("p-drawer-enter-active"),
      )
    );
  });
}

/** The modal dialog with this title: the item, recipient and generate dialogs
 *  and the message box. Named by its header, since a drawer and a dialog on top
 *  of it are both dialogs at once. */
export function dialog(page: Page, title: string | RegExp): Locator {
  return page.getByRole("dialog", { name: title });
}

/** The yes/no question asked before a step that discards something. */
export function confirmation(page: Page): Locator {
  return page.getByRole("alertdialog", { name: "Bitte bestätigen" });
}

/** The side drawer the invoice, grave and grave-invoice editors open in. Its
 *  header is not wired to it as a name, so it is found by its class. */
export function drawer(page: Page): Locator {
  return page.locator(".p-drawer");
}

/**
 * A row's action button, by what it does. Every row carries the same buttons,
 * so each one's accessible name also says which row it acts on ("Details:
 * 2026-0042") — this matches the action whichever row it belongs to.
 */
export function rowAction(name: string): RegExp {
  return new RegExp(`^${name.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}(: |$)`);
}

/** The toasts: a failed call or a failed client check shows one, and so does
 *  a saved setting. */
export function toasts(page: Page): Locator {
  return page.locator(".p-toast-message");
}

/**
 * Picks one option from a PrimeVue Select, and does not return until the pick
 * has landed.
 *
 * Like a UI5 Select, it is not finished when the click returns: the overlay
 * hides on a zero timeout and only then hands focus back to the field. A spec
 * that opens the next Select before that has its overlay closed again by the
 * pending focus change. So this waits for the overlay to have finished
 * entering before it types or clicks, and for it to be gone and the field to
 * show the pick before returning.
 *
 * The option list can also change under the pick. The item and recipient
 * pickers reload their lists whenever an editor opens, and on a slow machine
 * the answer arrives while the overlay is already open: it replaces the option
 * about to be clicked, and the overlay that realigns to the new list can close
 * on the scroll that brings the option within reach. Rather than guessing when
 * the list has settled, a pick that did not land is simply made again.
 *
 * `filter` types into the overlay's own filter field first, for the Selects
 * that have one (items and recipients). Options are named by their label.
 */
export async function pickOption(
  page: Page,
  select: Locator,
  option: string,
  { filter, shows = option }: { filter?: string; shows?: string } = {},
): Promise<void> {
  const overlay = page.locator(".p-select-overlay");
  // a Select ignores clicks while it shows its loading icon
  const loading = select
    .locator("xpath=ancestor-or-self::*[contains(concat(' ', @class, ' '), ' p-select ')][1]")
    .locator(".p-select-loading-icon");
  const step = { timeout: 3000 };

  await expect(async () => {
    if (!(await overlay.isVisible())) {
      await expect(loading).toHaveCount(0, step);
      await select.click(step);
    }
    await expect(overlay).toBeVisible(step);
    await expect(overlay).not.toHaveClass(/p-connected-overlay-enter-active/, step);
    if (filter !== undefined) {
      await overlay.locator("input.p-select-filter").fill(filter, step);
    }
    await overlay.getByRole("option", { name: option, exact: true }).click(step);

    await expect(overlay).toHaveCount(0, step);
    await expect(select).toContainText(shows, step);
  }).toPass({ timeout: 30_000 });
}

/** Picks an item in one line of a line editor, by its number. */
export async function pickItem(page: Page, line: number, itemNumber: string): Promise<void> {
  await pickOption(page, page.getByRole("combobox", { name: `Nummer ${line}` }), itemNumber, {
    filter: itemNumber,
  });
}

/** Picks a recipient in the picker with this input id, by name. */
export async function pickRecipient(page: Page, inputId: string, name: string): Promise<void> {
  await pickOption(page, page.locator(`#${inputId}`), name, { filter: name });
}

/**
 * Searches a list and waits for the answer, not just for the request.
 *
 * The lists debounce their search field by 300ms and keep the previous rows on
 * screen until the new ones arrive, so a row count taken straight after typing
 * is satisfied by the stale rows — the trap invoices.spec.ts describes for UI5.
 */
export async function search(
  page: Page,
  field: Locator,
  term: string,
  answer: (url: string) => boolean,
): Promise<void> {
  // a query option's value spells its spaces as "+"
  const answered = page.waitForResponse((response) =>
    answer(decodeURIComponent(response.url().replace(/\+/g, " "))),
  );
  await field.fill(term);
  await answered;
}

/**
 * Saves the editor in a drawer and waits for it to close. Saving an invoice
 * renders its documents behind the blocking overlay before the drawer closes,
 * which on a slow machine takes longer than an assertion's default wait.
 */
export async function saveEditor(editor: Locator): Promise<void> {
  await editor.getByRole("button", { name: "Speichern" }).click();
  await expect(editor).toHaveCount(0, { timeout: 30_000 });
}

/**
 * Types into a PrimeVue InputNumber. The field formats and hands its value on
 * when it loses focus, not while it is typed into — a total that depends on it
 * only follows once the field is left — so this leaves it, as a user moving on
 * to the next field does.
 */
export async function fillNumber(field: Locator, value: string): Promise<void> {
  await field.fill(value);
  await field.blur();
}

/**
 * Types a date into a DatePicker. The field takes the typed text as it goes,
 * but the calendar it opens stays over the fields beneath it until something
 * closes it; Escape does, without touching the value.
 */
export async function fillDate(page: Page, field: Locator, date: string): Promise<void> {
  await field.fill(date);
  await field.press("Escape");
  await expect(page.locator(".p-datepicker-panel")).toHaveCount(0);
  await expect(field).toHaveValue(date);
}
