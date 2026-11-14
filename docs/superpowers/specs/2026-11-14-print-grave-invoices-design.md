# Print all grave invoices of a half-year as one PDF — design

Status: Approved

## Intent

Grave invoices go out on paper. Today the user downloads the half-year's ZIP and
prints every PDF in it one by one. A "Drucken" button next to the ZIP button on
the grave page opens all of them as **one** PDF in a new browser tab, so the
whole half-year prints in one print job.

## Goals

- One click on the grave page opens one PDF holding every grave invoice of the
  selected half-year, in a new tab, for printing from the browser's viewer.
- The PDF holds exactly what the ZIP holds, in the same order: the stored normal
  documents of the latest revisions, no copies, no invoices of deleted graves
  (FR-045, FR-046, FR-065), ordered by file name, i.e. by invoice number.
- The pages are the stored PDFs as they are — what the customer received.

## Non-goals

- No duplex support: printing is single-sided, so no blank pages are inserted
  between invoices (decided by the user).
- No copies, no non-grave invoices, no choice of individual graves.
- The ZIP download stays as it is.

## Anti-goals

- A printout that differs from the stored invoice (re-rendered texts, letterhead).
- A tab showing a JSON error when there is nothing to print.

## Approaches

1. **The server joins the stored PDFs (chosen).** A new route serves the
   half-year's normal documents, selected exactly as the ZIP selects them,
   joined into one PDF and served inline. Prints exactly what is stored and
   follows the pattern of the ZIP and the lists.
2. The server renders all invoices anew as one document — rejected: the result
   can drift from the stored invoices.
3. The browser fetches every PDF and joins them — rejected: a new frontend
   dependency and one request per invoice, for no gain over 1.

## The how, as decided

- **API**: `GET odata/GraveInvoicesPdf?year=&half=` answers `application/pdf`,
  `Content-Disposition: inline`, named `Grabrechnungen-2027-1.pdf`
  (year, half). A missing or invalid year or half is refused with
  `InvalidHalfYear`, as the ZIP route refuses it. A half-year without
  documents is refused with a new code `NoGraveInvoices` (message
  "Für 1. Halbjahr 2027 gibt es keine Grabrechnungen." style, German text in
  the frontend's i18n like the other codes).
- **Selection**: the ZIP's document query is moved into one shared place and
  used by both routes, so the two cannot drift apart.
- **Joining**: QuestPDF's `DocumentOperation` (merge, already a dependency).
  If it does not work in the release image, PdfPig's `PdfMerger` (already used
  by the tests, Apache-2.0) is the fallback. My call; the plan pins it with a
  test that the joined PDF has the sum of the pages and each invoice's number
  in order.
- **Frontend**: a button "Drucken" with `pi pi-print` next to the ZIP button,
  busy while the PDF is made. It uses `openPdf` from `lib/download.ts` (tab
  opened before the await, closed again on a refusal). Like the ZIP button, it
  shows the existing "nothing there" info message without a request when the
  status list shows no invoices for the half-year.
- **Spec**: User Story 9 and FR-045 gain the print action; a new FR for the
  route.
- **Tests**: integration test for the route (selection matches the ZIP's,
  inline, name, refusals), an E2E test that the button opens a tab whose
  address serves the inline PDF, and the layout spec's grave page still fits
  the phone width with the extra button.

## Left to the builder

- Where the shared selection lives (a small service or a static query helper).
- Exact wording of the refusal message and the button's tooltip/aria label.
