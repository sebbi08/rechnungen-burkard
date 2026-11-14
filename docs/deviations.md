# Recorded Deviations

The constitution requires that deviations from its principles are justified and
recorded. This file is that record. Everything not listed here is expected to
follow the constitution as written.

## D-001: Some `/odata` endpoints are not part of the EDM

**Principle**: I — Full-Stack Web Application (OData v4 as the documented
interface).

**Deviation**: `SaveGrave`, `GraveInvoicingStatus`, `BulkDownloadDocuments`,
`ImportTemplate`, `ImportArchive`, `DownloadBackup`, `UploadBackup`,
`RestoreBackup`, `DiscardBackup`, `GraveInvoiceGroup`, `ItemGraves`,
`ValidateGraveBilling` and `PreviewGraveBilling` are plain MVC routes mounted
under the `/odata` prefix. They do not appear in `$metadata`.
`Documents({key})/$value` is one too: it streams a document's PDF, while the
EDM declares `Document` without a media stream and keeps its content out.

**Why**: Their payloads are not entity graphs — a ZIP download, a multipart
upload, a backup file and a token for restoring it, a validation report, a half-year status projection, a PDF. Modelling
them as EDM operations would force artificial entity types on responses that
are files or reports, without making them any easier to consume.
`GraveInvoiceGroup` reports the grave-level amounts behind a group of invoices,
which is a report about several entities rather than any one of them;
`ItemGraves` likewise reports which graves bill an item, grouped by the price
they bill. `ValidateGraveBilling` and `PreviewGraveBilling` check a grave that
is still being edited and is not stored: there is no entity to call them on.

**Containment**: Their reports answer with the same PascalCase property names
the EDM serializer produces (`ODataJsonDefaults.PascalCase`), so a client reads
data in one casing across the whole API. Two kinds of answer are camelCase
instead, and the frontend reads them so: every error body
(`{error: {code, message, args}}`, the same on EDM and non-EDM routes) and
`ImportArchive`'s summary and problem report (`{summary: …}`, `{problems: […]}`),
and `UploadBackup`'s answer (`{token, summary: {…}}`).
All entity data — everything the UI lists, filters and sorts — goes through
the EDM.

## D-002: The frontend writes partly through a hand-typed module

**Principle**: Technology Constraints — "consumed by the frontend through a
client generated from `$metadata`".

**Deviation**: Every read and most entity writes (items, VAT rates, the
settings, editing a recipient) go through the client odata2ts generates from
`$metadata`. The D-001 endpoints, the actions whose parameters `$metadata`
leaves out, and creating a recipient go through `fetch`, in the hand-typed
module `frontend/src/api/endpoints.ts`.

**Why**: The D-001 endpoints are not part of the EDM, so nothing can be
generated for them, and an action whose parameters the EDM does not declare
would be generated without them. A new recipient may leave its number out for
the backend to assign, which the generated create type, with the number
required, cannot express.

**Containment**: All of these calls are in one module with a single error
mapping (`frontend/src/api/http.ts`), so the client-side contract stays in one
place. The E2E suite covers each of them end to end.

## D-003: Withdrawn

A second frontend in Vue, built to decide whether to replace the OpenUI5 one.
It did: constitution 4.0.0 makes Vue and PrimeVue the frontend (Principle VI),
and the OpenUI5 frontend has been removed.

## D-004: v1.0.0 shipped three migrations

**Principle**: IV — SQLite Persistence with Versioned Migrations ("each version
MUST introduce at most one migration").

**Deviation**: The first release, v1.0.0, brought three migrations:
`20260822173058_InitialCreate`,
`20260828155506_GraveItemPriceRecipientNumberAndLanguage` and
`20260829064353_LatestRevisionAndGraveUnitPrice`.

**Why**: They were written while the app was built, before anything was
released; they were not consolidated before the first release was cut.

**Containment**: No database predates v1.0.0, so every installation applies the
three together, as one would apply a single migration. They are released and
applied now, so they stay as they are: rewriting them would break databases
that recorded them. Every release since has introduced at most one migration.

## D-005: v3.5.0 ships three migrations

**Principle**: IV — SQLite Persistence with Versioned Migrations ("each version
MUST introduce at most one migration").

**Deviation**: The release after v3.4.0 brings three migrations:
`20261112174329_SeparateEndingTexts` (four ending texts, issue 66),
`20261112212917_DocumentComments` (free text above the lines and line
comments, issues 63 and 65) and `20261114084602_GrossLinePrices` (the gross a
line was entered as, and no VAT-included grave prices, issue 73).

**Why**: The first was already on `main`, and applied to the development
database, when the second was written, and the second likewise when the third
was. Folding new columns into a migration that a database had already recorded
would have changed it without EF Core noticing: that database would never get
them. The owner chose a further migration each time over releasing in between.

**Containment**: The first two only add columns with defaults, plus one data
copy in the first. The third adds two nullable columns and drops one, after a
data conversion that touches no row in the owner's data (no grave link has the
flag set); it exists so a database that does have one bills what it billed
before. All three apply in one go on upgrade like a single migration would. If
a release is cut between any of them, this deviation no longer applies and is
to be removed.
