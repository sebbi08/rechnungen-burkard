import type { GraveInvoiceRecipient, GraveStatusRow, ImportProblem, YearHalfOption } from "./api";

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

/** The report shown after a rejected archive. */
export interface ImportView {
  problems: ImportProblem[];
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
  /** The ComboBox above the recipient list, before "add" moves it into Recipients. */
  linkRecipientId: string;
  linkRecipientName: string;
}

export interface GraveInvoiceDraft {
  invoiceId: number;
  warning: string;
  lines: DraftLine[];
}

/** One line of a revision, with its total worked out. */
export interface RevisionLine {
  Count: number;
  UnitPrice: number;
  VatRate: number;
  ItemNumber: string;
  Description: string;
  LineTotal: number;
}

export interface Revision {
  Id: number;
  InvoiceNumber: string;
  RevisionNumber: number;
  GeneratedAt: string;
  Title: string;
  /** The current revision opens; the history stays collapsed. */
  Expanded: boolean;
  Lines: RevisionLine[];
  NetTotal: number;
  VatTotal: number;
  GrossTotal: number;
}

/**
 * An invoice chain as the two detail pages show it. The grave fields stay empty
 * on a plain invoice; only the grave-invoice page fills them.
 */
export interface RevisionDetailView {
  invoiceNumber: string;
  recipientName: string;
  recipientAddress: string;
  /** The edit dialog reopens on this recipient. */
  recipientId: number;
  latestInvoiceId: number | null;
  graveId: number | null;
  periodYear: number | null;
  periodHalf: number | null;
  halfYear: string;
  sharePercent: number | null;
  revisions: Revision[];
  graveNumber: string;
  shareText: string;
  /** The siblings of this half-year, minus the invoice being shown. */
  others: GraveInvoiceRecipient[];
}

/** A detail page with nothing to show: an unknown or deleted invoice. */
export function emptyRevisionDetail(): RevisionDetailView {
  return {
    invoiceNumber: "",
    recipientName: "",
    recipientAddress: "",
    recipientId: 0,
    latestInvoiceId: null,
    graveId: null,
    periodYear: null,
    periodHalf: null,
    halfYear: "",
    sharePercent: null,
    revisions: [],
    graveNumber: "",
    shareText: "",
    others: [],
  };
}
