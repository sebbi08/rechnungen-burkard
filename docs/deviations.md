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

## D-003: A second frontend in Vue, for evaluation only

**Principle**: VI — OpenUI5 Frontend; and the Technology Constraints line
"consumed by the UI5 frontend's OData V4 model".

**Deviation**: `frontend-vue/` is a second frontend, built with Vue 3 and
PrimeVue 4 (MIT). It covers the same features as the OpenUI5 frontend and talks
to the same backend through a client generated from `$metadata` (odata2ts),
plus a hand-typed module for the D-001 endpoints and the actions whose
parameters `$metadata` leaves out.

**Why**: To decide whether to replace the OpenUI5 frontend on the strength of a
like-for-like build rather than a sketch. Replacing it would amend Principle VI;
this entry does not.

**Containment**: The OpenUI5 frontend stays the application's frontend: the
release image serves it on port 8080 as before. The image also carries the Vue
build and serves it on a second port, 8082, from the same process and against
the same API, so the two can be compared on real data; in development it runs
from `docker-compose.yml` on 8082 as well. Nothing on the UI5 port changes, and
the bundled dependencies' licenses ship with the Vue build. It uses the documented OData interface unchanged, keeps the UI multilingual
(VII) and blocks the UI during long-running tasks (VIII). It has no automated
tests by decision; CI type-checks, lints and builds it.
