import type { GraveStatusRow, YearHalfOption } from "./api";

/**
 * What a view or a dialog holds while the user works in it.
 *
 * Every interface here backs a TypedJSONModel the XML binds to, so a control
 * never has to be asked what it is showing. Two kinds of model use them:
 * a "view" model for page state (search terms, toolbar selections) and a
 * "<thing>Draft" model for everything a dialog collects until it is saved.
 *
 * Select and ComboBox keys are strings, so every id a control selects is a
 * string here and is converted where the API is called.
 */

/**
 * A line in the invoice, grave and grave-invoice dialogs. All three collect the
 * same fields — an item, how many, and what it costs on this document.
 */
export interface DraftLine {
  itemId: string;
  itemNumber: string;
  description: string;
  /** A two-way bound Input writes a string back, so both shapes occur. */
  count: number | string;
  /** Empty means "use the default"; 0 is a real price. */
  unitPrice: number | string | null;
  defaultPrice: number | string | null;
}

export interface ItemsView {
  /** null rather than "" — a null filter value is neutral and drops the filter. */
  searchTerm: string | null;
}

export interface ItemDraft {
  /** null while creating, the item's id while editing. */
  id: number | null;
  number: string;
  description: string;
  vatRate: string;
  /** Kept as typed: "" means no default price, which is not the same as 0. */
  defaultPrice: string;
}

export interface RecipientsView {
  searchTerm: string | null;
}

export interface RecipientDraft {
  id: number | null;
  /** Empty on create means "assign the next free number". */
  number: string;
  name: string;
  address: string;
}

export interface SettingsView {
  newVatRate: string;
}

export interface SettingsDraft {
  stampWord: string;
  endingText: string;
  language: string;
}

/** The rows the grave overview shows: the half-year status order, filtered. */
export interface GravesStatus {
  graves: GraveStatusRow[];
}

export interface GravesView {
  halfYearOptions: YearHalfOption[];
  /** "2026-1" — year and half, as the select's key. */
  halfYear: string;
}

export interface GenerateDraft {
  yearOptions: YearHalfOption[];
  year: string;
  half: string;
}

/** Shared by the invoice and grave dialogs: both load the same fragment. */
export interface QuickRecipientDraft {
  name: string;
  address: string;
}

export interface InvoiceDraft {
  /** Set when revising an existing invoice, null when creating one. */
  invoiceId: number | null;
  recipientId: string;
  lines: DraftLine[];
}

export interface GraveRecipientRow {
  recipientId: string;
  name: string;
}

export interface GraveDraft {
  Id: number | null;
  GraveNumber: string;
  StartDate: string;
  EndDate: string;
  Items: DraftLine[];
  Recipients: GraveRecipientRow[];
  /** The select above the recipient list, before "add" moves it into Recipients. */
  linkRecipientId: string;
  linkRecipientName: string;
}

export interface GraveInvoiceDraft {
  invoiceId: number;
  warning: string;
  lines: DraftLine[];
}
