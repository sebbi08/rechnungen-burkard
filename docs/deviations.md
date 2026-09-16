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

## D-002: The frontend writes through `fetch`, not the OData V4 model

**Principle**: I — "consumed by the UI5 frontend's OData V4 model".

**Deviation**: Every read (list bindings, filters, sorting, `$select`,
`$expand`) uses `sap.ui.model.odata.v4.ODataModel`. Writes go through `fetch`
in `frontend/src/model/api.ts`.

**Why**: The write paths are actions and file transfers rather than entity
updates, and the backend exposes no `$batch` endpoint. Routing them through the
model would add a deferred-change layer the app never uses, while the actions
above (D-001) are not model-addressable at all.

**Containment**: All write endpoints are in one module with a single error
mapping, so the client-side contract stays in one place. The E2E suite covers
each write path end to end.
