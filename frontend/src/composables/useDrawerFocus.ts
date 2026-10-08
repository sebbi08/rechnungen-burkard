import { nextTick } from "vue";

/** What PrimeVue's Dialog does and its Drawer does not: focus goes to the
 *  first field when the drawer opens -- not to its close button -- and back to
 *  whatever opened it when it closes, instead of to <body>, where a keyboard
 *  user starts again from the top of the page. */
export function useDrawerFocus() {
  let opener: HTMLElement | null = null;

  /** Call when the drawer is asked to open, before it takes focus. */
  function remember(): void {
    opener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
  }

  /** For the drawer's @after-hide. */
  function restore(): void {
    if (opener?.isConnected) {
      opener.focus();
    }
    opener = null;
  }

  /** Focuses the first field (or, in the menu, link) inside `root`, once it is rendered -- after the
   *  drawer has opened, or after an edit's data has loaded into it. */
  async function focusFirst(root: HTMLElement | null | undefined): Promise<void> {
    await nextTick();
    root
      ?.querySelector<HTMLElement>(
        'input:not([type="hidden"]):not([disabled]):not(.p-hidden-accessible), textarea, [role="combobox"][tabindex="0"], a[href]',
      )
      ?.focus();
  }

  /** For the drawer's @keydown.capture, with the drawer's own Escape turned off
   *  (`:close-on-escape="false"`). PrimeVue's Drawer listens for Escape on the
   *  whole document, so an Escape meant for something else -- a list's search
   *  field or a calendar, both drawn outside the drawer -- closed the editor
   *  as well. Here only an Escape from inside the drawer is the drawer's, and
   *  not one on a field whose list or calendar is open: that one closes just
   *  its list.
   *
   *  The Escape is claimed: closing may ask first, and the question's dialog
   *  is drawn between this listener and the next, so the same key travelling
   *  on to the document reached the dialog's own Escape and dismissed it
   *  before it was seen. */
  function onKeydown(event: KeyboardEvent, close: () => void): void {
    const target = event.target;
    if (
      event.key === "Escape" &&
      !(target instanceof HTMLElement && target.getAttribute("aria-expanded") === "true")
    ) {
      event.stopPropagation();
      close();
    }
  }

  return { remember, restore, focusFirst, onKeydown };
}
