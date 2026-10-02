# Recorded Deviations

The constitution requires that deviations from its principles are justified and
recorded. This file is that record. Everything not listed here is expected to
follow the constitution as written.

## D-001: Some `/odata` endpoints are not part of the EDM

**Principle**: I — Full-Stack Web Application (OData v4 as the documented
interface).

**Deviation**: `SaveGrave`, `SearchGraves`, `GraveInvoicingStatus`,
`BulkDownloadDocuments`, `ImportTemplate`, `ImportArchive`, `GraveInvoiceGroup`
and `ItemGraves` are plain MVC routes mounted under the `/odata` prefix. They do
not appear in `$metadata`.

**Why**: Their payloads are not entity graphs — a ZIP download, a multipart
upload, a validation report, a half-year status projection. Modelling them as
EDM operations would force artificial entity types on responses that are files
or reports, without making them any easier to consume. `GraveInvoiceGroup`
reports the grave-level amounts behind a group of invoices, which is a report
about several entities rather than any one of them; `ItemGraves` likewise
reports which graves bill an item, grouped by the price they bill.

**Containment**: They answer with the same PascalCase property names the EDM
serializer produces (`ODataJsonDefaults.PascalCase`), so a client sees one
casing across the whole API. All entity data — everything the UI lists, filters
and sorts — goes through the EDM.

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
