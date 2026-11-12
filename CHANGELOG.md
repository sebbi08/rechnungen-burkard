# Changelog

Each release adds a section here, generated from the commit messages since the
previous tag by `scripts/changelog-entry.sh` and written by the release
workflow. Housekeeping commits — docs, tests, chores, CI, style, build — are
left out, so this records what changed for someone using the application.

## v3.3.0 — 2026-11-12

### Features
- fold the panels of the inactive graves away (026846c)
- show ten entries a page in every list (f143b03)
- delete a grave from the lists, keep the confirmation narrow (a0df077)
- list ended and deleted graves on a page of their own (4b5fa31)
- delete and restore a grave from its page, list only current cooperative graves (5e063e0)
- keep deleted graves and their invoices out of runs, lists and payments (2e47ee3)
- delete a grave and restore it until the next invoice run (fa0931b)
- **scripts**: classify cooperative graves by contract number and 2026 care invoices (6df2745)
- show, search and require the contract number of a cooperative grave (3f4231e)
- import contract numbers and ordinary graves still to be completed (347605f)
- give a cooperative grave its contract number and free recipients until the first invoice (0593310)
- list only the graves the business bills in the grave overview (808d3f4)

### Fixes
- show the deletion date in local time, share the half-year start month (ea31aad)
- refuse deleting or restoring a grave while an invoice run is active (af7183a)
- hide a deleted grave's preview and status, wait for its state (32721b7)
- keep an item locked while only a deleted grave links it (bab610b)
- **scripts**: restore the FriedWin README sections, report billed graves' customer record (05fcc3c)
- keep an ordinary grave's recipients from being emptied by a save (abb8aa7)
- load the cooperative graves without a semicolon in the query (b2b3af5)

### Other changes
- merge the unreleased grave migrations into one (9330328)

## v3.2.0 — 2026-11-09

### Features
- **scripts**: import every active FriedWin grave, the unbilled ones as cooperative graves (0cb4c7a)
- list the cooperative graves on a page of their own (fd46487)
- mark cooperative graves in the overview, on the detail page and in the item dialog (dcfe7cd)
- tick, untick and hand over a cooperative grave in the editor (6510aff)
- import cooperative graves from an optional Cooperative column (89ff23f)
- type the cooperative flag in the frontend API (39d4f78)
- hand a grave over between the cooperative and the business from a month on (a3f23aa)
- list cooperative graves in the overview, the item dialog and the graves set (b3fbb63)
- never invoice a cooperative grave (17ddd8d)
- let a grave be a cooperative grave that needs no recipient or price (7b5f937)
- **frontend**: search the grave overview by column, with its recipients (42c6e43)
- **api**: carry the cemetery id on the grave status rows (ef0cf5d)
- **frontend**: search the item list by column (98c1751)
- **frontend**: search the invoice list by column and show its totals (c60eb3b)
- **frontend**: search the recipient list by column (344f707)
- **frontend**: column filter state, filter row cell and filter sheet (71968ff)
- **api**: carry a grave's recipients on its status row (7fd6a79)
- **api**: search invoices and recipients column by column (d018c6d)
- give each grave a cemetery of its own, kept in the settings (7a8efc0)
- search every word of a term, in any order and any field (095357a)
- **frontend**: offer the newest twenty recipients in the picker and search the rest (a929809)
- **frontend**: show lists a hundred rows a page with the paginator always in view (adbb972)
- **import**: take e-mail addresses, prices per grave and more grave positions (6835d6e)

### Fixes
- let the cooperative status tag break on a phone (4b9278f)
- keep the cooperative graves marked when one of them is opened (f3de9cf)
- **scripts**: keep the field number of an unnamed cooperative grave (160bf61)
- hide the billing preview and outdated tag of cooperative graves on the detail page (0cd3972)
- ignore a start block beyond the generation window when handing a grave over (ff9832d)
- **scripts**: read FriedWin's customer fields at full width and the cemetery from the grave record (f0e21b1)
- **invoices**: allocate the invoice number inside the write transaction (e1f6c58)
- **deps**: update dependency primeicons to v8 (#35) (6dbf958)
- **deps**: update dependency primevue to v5 (#36) (d542753)
- **deps**: update dependency @primeuix/themes to v3 (#34) (66ccc79)
- **deps**: update dependency vue-router to v5 (#37) (f1fee94)
- **frontend**: keep the invoice list's row actions on screen on tablets (d809d7f)
- match amounts alike in the invoice total and the item price searches (a874e83)
- **frontend**: keep a list's filters on the breadcrumb, and write them once settled (fde6fe8)
- **frontend**: keep the grave overview inside its card on laptop screens (cb297cd)

### Other changes
- **api**: drop the SearchGraves endpoint (8307a1b)
- **api**: drop the single-term invoice search option (6a1cdca)
- drop the rounding-corrections filter and tag from the grave overview (a2ed05e)

## v3.1.0 — 2026-10-08

### Features
- **import**: read CSV files that Excel saved with semicolons (b99ae00)
- report the running version and the database's health (c5fa1f3)

### Fixes
- **frontend**: small fixes from the review (f6deeab)
- **api**: answer malformed requests with a refusal instead of a 500 (a446def)
- **api**: refuse a second generation once every due grave has its invoices (d15ed4b)
- **api**: number recipients created at once without a race (8aa30fd)
- **api**: download exactly one half-year's grave invoices in bulk (1da2cca)
- say the screen-reader texts and the PDF's properties in German (9d22788)
- **frontend**: keep a running generation in view and ask before an editor drops input (7ee484d)
- **import**: refuse a file that is not UTF-8 and keep row numbers true (eb4f6cf)
- revalidate the page on every load so a release never shows a blank one (49f2230)
- **jobs**: end a job failed when its outcome cannot be written (59b284b)
- **frontend**: keep the recipient list within a phone's screen (087fa14)
- **import**: read a German decimal price at its value, not a hundred times it (9e7fdc2)
- date invoices by the local day and hold a revised grave price to the cent (173d777)
- **frontend**: let long item numbers wrap in the grave billing preview (bbc46b0)
- **frontend**: let long item and grave numbers wrap on small screens (e1f58aa)

### Other changes
- bring stale comments up to date and guard the share division (320ed26)

## v3.0.0 — 2026-10-07

### Breaking changes
- **import**: read an item's quarter, cap recipients and check a term's invoices (59fb42e)
- **import**: import grave site fields and month terms (f7e02f8)
- make the application German only (4d31f0a)
- make the Vue frontend the application's frontend and remove OpenUI5 (42e48b3)

### Features
- **frontend**: filter the graves by any year with graves (1b20b08)
- **frontend**: pick the year by calendar and the half-year by toggle when generating (fbad608)
- **generation**: generate at most two half-years back (bddce36)
- **billing**: leave the start block of a started term unchecked (9ae649b)
- **frontend**: filter graves with rounding corrections and explain a stopped run (5a6f0eb)
- **frontend**: show rounding corrections and confirm them before saving a grave (cd4b0b2)
- **import**: store rounding corrections and drop the price suggestions (17605be)
- **generation**: bill stored corrections and stop a run whose corrections are outdated (349c079)
- **graves**: preview the rounding corrections of every invoice (0dbe56b)
- **graves**: store rounding corrections a save confirmed (8fe1fe5)
- **billing**: correct a block that does not split by raising one line per rate (da1c83b)
- **graves**: require at least one all-year item on every grave (471c494)
- **frontend**: preview a grave's invoices while it is edited and on its page (fc32be2)
- **frontend**: show an item's quarter, offer line price suggestions, render billing previews (04124f6)
- **frontend**: read item quarters and the grave billing preview (210a4cc)
- **generation**: bill only the months of a grave's term and the quarters they touch (e2b3889)
- **graves**: preview every invoice of a grave's term with per-line price suggestions (c8486ca)
- **graves**: refuse a grave unless every invoice of its term divides, and cap it at four recipients (d19f612)
- **billing**: check every invoice of a grave's term and suggest prices that fix all of them (2273951)
- **billing**: bill a half-year by the months of the term and the items' quarters (0c3daf6)
- **items**: give an item an optional billing quarter, fixed once a grave bills it (b5a96bd)
- **frontend**: edit grave site fields and month terms (6f2ede5)
- **documents**: lay invoices out like the scanned originals (a4b7529)
- **invoices**: snapshot the recipient number and the grave site (e52ec2e)
- **graves**: save site fields and month terms, keep grave numbers unique in time (4e287a6)
- give each import problem a message key and its values (fb21c6c)

### Fixes
- **frontend**: read the earliest generatable half-year when the dialog opens (3250e99)
- **billing**: check every start block that can still be generated (8bbe53b)
- **frontend**: keep the overview's year while its field is emptied (a716572)
- **frontend**: show the rounding tag next to the invoicing status (9613558)
- **graves**: report a missing recipient before changed corrections (1d815cf)
- **frontend**: list corrected graves like the other import counts (197791a)
- **frontend**: refresh the preview on changed corrections and block a double confirm (bc63b2b)
- **frontend**: read outside the term when no item bills in the half-year (9c3263c)
- **backend**: bill a grave only in half-years where its links bill a line (2a07483)
- **frontend**: show the items' quarter column from lg so the tablet row actions fit (48dd43b)
- **frontend**: keep the metadata snapshot in its wrapped format (f0c88fc)
- **revisions**: keep a grave invoice line's prorated description when it is edited (48b47a2)
- **frontend**: show a dash for missing grave site fields (5a0cefb)
- **documents**: print the billed gross on VAT-included lines (13d3f32)
- **frontend**: show the grave site column from lg so the tablet table fits (35a33ed)
- **vue**: guide focus, name controls and finish the screens (af2d53f)
- **vue**: make the lists' search, counts, dates and downloads say what they do (9b607db)
- **vue**: stop number fields from silently changing what was typed (b782453)
- refuse a used VAT rate's deletion, rates of 100 %, sub-cent prices and reversed grave dates (f90acbc)
- **vue**: show the graves search as loading while it runs (2e97688)
- **vue**: show a stored date as the calendar day it is (43696ae)
- **vue**: show a grave invoice only under its own grave (8cbb05e)
- **vue**: drop an editor's late load once the drawer moved on (0606507)
- **vue**: explain why a grave invoice revision does not split (0b6fe61)
- refuse a grave invoice revision whose prices do not split (96060ea)
- **vue**: keep tables and their row actions within phone and tablet widths (5fe6e39)
- **vue**: clear import problems after a successful import (4f2bc5d)
- **vue**: default a new invoice to the recipient that is newest now (2085d55)
- **vue**: stop a second Save from creating an invoice twice (8d3a566)
- **vue**: keep an item's VAT rate locked until its graves are known (d75714b)
- **vue**: escape quotes in item and recipient search terms (14bc4e6)

### Other changes
- Revert "test(e2e): drop the phone known-bug mark for the items list" (29f1ca0)
- **documents**: pass the invoice snapshot and VAT-included lines to the document (bf56177)
- **db**: add grave site fields and invoice snapshots in one consolidated migration (a83f068)
- Merge remote-tracking branch 'origin/main' into worktree-bridge-cse_01SnxByVoXPpGkXTSna4xTsq (b4dfa7e)
- Merge pull request #13 from sebbi08/ccr-718205f7-2otaxy (7701267)
- Merge remote-tracking branch 'origin/main' into ccr-718205f7-2otaxy (004232b)

## v2.1.0 — 2026-09-28

### Features
- serve the Vue evaluation frontend from the release image on port 8082 (9f1e53f)
- **graves**: make creating a grave the list's main action (9b36077)

### Fixes
- **dev**: hand the node_modules volumes to the container user (b024ee5)

### Other changes
- Merge pull request #12 from sebbi08/vue-evaluation-frontend (576c63a)

## v2.0.0 — 2026-09-16

### Breaking changes
- **api**: write graves only through SaveGrave (757a529)
- **items**: freeze a billed item's VAT rate and keep graves on their price (2dfff69)
- **graves**: refuse a price with more than two decimals wherever it is entered (e998a91)
- **graves**: refuse an unpriced item in the import and the live check (31a746c)
- **graves**: refuse a grave without recipients (abaf2fc)
- **api**: gate the grave set's POST and drop its PATCH and DELETE (2b38895)
- **graves**: refuse prices that cannot be split instead of adjusting them (7304e11)

### Features
- **import**: refuse an archive whose graves cannot be split (bfa7176)
- **api**: offer the grave link writes only where seeding is the point (5f1f927)
- **graves**: show a grave's price as entered, without net and gross (975c77e)
- **invoices**: break the item tables into net, gross, rate and VAT flag (3a18202)
- **graves**: answer what a grave would be billed before it is invoiced (c2b20f7)
- **graves**: show what the grave was billed (0e460f0)
- **invoices**: serve invoice totals from the backend (6fef08a)
- **graves**: revise a grave bill in the share form (64d4835)
- **documents**: print the grave in full and the recipient's share (212f02a)
- **graves**: bill every recipient the grave's own lines (aa2dbf7)
- **graves**: raise a grave's prices until its subtotals divide (c1426ca)
- **graves**: tick VAT-included on a grave's item (b6ddedb)
- **invoices**: tick VAT-included on the invoice line (cecfd35)
- **import**: carry the VAT-included flag on the mapping row (3b96222)
- **invoices**: convert a price the line was entered as gross (ec6d2b8)
- **graves**: say how many of a half-year's invoices are paid (d51fd73)
- **invoices**: carry the VAT-included flag on the line, not the item (5e94d2a)
- **frontend**: payment status on the invoice and grave detail pages (381f724)
- **graves**: show and filter a half-year's payment state (70cd8a2)
- **invoices**: mark invoices paid and filter the list by payment (2cbc219)
- **frontend**: shared payment status texts and actions (40050dc)
- **graves**: report how many of a half-year's invoices are paid (67b9c7d)
- **invoices**: refuse to revise a paid invoice (ac267bd)
- **invoices**: mark an invoice as paid or unpaid (5822599)
- **invoices**: record whether an invoice is paid (fe40595)
- **graves**: enter a VAT-included price as its gross (d097a45)
- **invoices**: enter a VAT-included price as its gross (2796a17)
- **items**: tick VAT-included in the item dialog (e6e1b7a)
- **import**: carry the VAT-included flag in items.csv (85d3e77)
- **graves**: keep VAT-included prices stable across revisions (3c3ceb8)
- **graves**: split a VAT-included price after taking its VAT out (04df3b5)
- **invoices**: bill a VAT-included item at its net price (9863314)
- **items**: mark an item as priced VAT-included (5e7c264)
- **domain**: take the VAT back out of a VAT-included price (39c2abd)

### Fixes
- **import**: say recipient for one, and write the rate like the amounts (4d5baab)
- **api**: read the seeding flag once and hide the routes before the body is read (b669413)
- **graves**: translate the PricesDoNotDivide skip reason (ea9c767)
- **invoices**: ignore a row action whose row has already been replaced (c76b74d)
- **graves**: work each rate's gross out at that rate, not at a blended one (bfde2dd)
- **graves**: name a lower and a higher price whichever way the split fails (b77db8d)
- **graves**: raise a split until the shares cover the bill (74de4c9)
- **db**: refuse a missing database before anything opens it (e2a41a5)
- **documents**: state the base each VAT amount was worked out from (5cea251)
- **graves**: read a grave's items in a defined order (9c23d99)
- **graves**: guard MakeDivisible against non-positive line counts (b391621)
- **documents**: print one VAT amount per rate on a grave invoice (478777d)
- **db**: stop patch-dev-db.sh corrupting the migration history (913ce3e)
- **invoices**: stop reading document PDFs when listing invoices (56cc73b)
- **graves**: close the revisions review's findings (012c315)
- **db**: guard patch-dev-db.sh against database states it does not expect (dab0cfd)
- **graves**: make the cent tie-break deterministic, not library-ordering (f002202)
- **e2e**: wait for a Select's picker to close before reopening it (52ce543)
- **vat-included**: close the final review's minor findings (cf550e2)
- **invoices**: show the invoice date and mark only real revisions (c952f70)
- **recipients**: retry the assigned number when two creates collide (9177d93)
- **frontend**: announce payment status and pin the paid binding's value shape (0d52829)
- **vat-included**: close the final review's minor findings (e7859f3)

### Other changes
- Merge pull request #11 from sebbi08/worktree-review-follow-ups (a988a49)
- Merge pull request #10 from sebbi08/remove-dev-db-patch (1a54512)
- Merge pull request #9 from sebbi08/worktree-seeding-routes-and-import-check (3801546)
- **api**: remove the grave link deletes instead of hiding them (81041cd)
- Merge pull request #8 from sebbi08/worktree-grave-share-on-invoice (1129e96)
- **graves**: require RecipientCount on every sibling, not just the first (17e7c27)
- **db**: consolidate the unreleased migrations into one (3d38471)
- Merge pull request #7 from sebbi08/worktree-vat-included-items (1dc8120)
- Merge branch 'main' into worktree-vat-included-items (b320f0d)

## v1.1.0 — 2026-08-31

### Features
- **documents**: print invoices on the company letterhead (786d9e7)
- search recipients by typeahead, with the address as secondary value (40096f6)

### Fixes
- **e2e**: wait out both focus changes around the recipient typeahead (cb5b459)
- spend the line dialogs' width on their fields, not on gutters (cdeb807)
- pre-fill an item's default price in the grave dialog (cfac005)

### Other changes
- Merge pull request #6 from sebbi08/letterhead-din-lang (f0c36bd)
- Merge pull request #4 from sebbi08/worktree-recipient-typeahead (358c74e)
- Merge remote-tracking branch 'origin/main' into worktree-recipient-typeahead (2467df0)
- Merge pull request #5 from sebbi08/worktree-mobile-grave-dialog-layout (1c2b73c)
- Merge remote-tracking branch 'origin/main' into worktree-recipient-typeahead (66df7ec)
- Merge pull request #3 from sebbi08/worktree-grave-dialog-default-price (76bd8cd)
- Merge pull request #2 from sebbi08/worktree-auto-formatting (2efcc6f)
- Merge remote-tracking branch 'origin/main' into worktree-auto-formatting (2f6b7b7)

## v1.0.2 — 2026-08-30

### Fixes
- open the generate dialog on the half-year today falls in (9cd89dc)
- update half-year default to current date (a30176a)

## v1.0.1 — 2026-08-30

### Fixes
- serve every resource the UI5 build ships from the release image (cc69d42)

## v1.0.0 — 2026-08-29

### Features
- a detail page for a grave invoice and its revisions (484f449)
- colour the half-year status and download a half-year's invoices (30388d7)
- edit a grave invoice for all of its recipients at once (a06f38b)
- a grave detail page with its whole invoicing history (f20318c)
- say what editing a grave does, and freeze its recipients (9391908)
- the invoice page lists non-grave invoices and searches them (f3521f9)
- an invoice detail page showing every revision (1d6d20a)
- freeze a grave's recipients after creation (da96047)
- editing a grave invoice revises every recipient (c7e1fcf)
- expose a grave invoice's group and a grave's invoice runs (74686d2)
- search invoices by number, recipient, date and total (d233bfb)
- mark the latest revision and remember the grave line price (428a85c)
- show single invoices in the browser instead of downloading them (2c99b23)
- usable UI on phones and tablets, plus two invoice dialog fixes (496f4f3)
- add experimental HMR middleware for hot module replacement (50edb82)
- typeahead item selection with text in invoice dialog (71a2006)
- use a VAT rate dropdown in the item dialog (b8bbe05)
- tile launchpad, UI5 1.151 and fiori spacing (7c65dfe)
- item/recipient editing, quick recipient creation, grave search and revision badges (f5739e6)
- grave management, half-year status and blocking generation (a5a0d97)
- invoice creation, revision and document downloads (66de4e5)
- settings, items and recipients views (8957582)
- UI5 TypeScript app shell with routing and E2E harness (2fec481)
- separate bulk downloads for normal documents and copies (58c57ca)
- half-year grave invoice generation with share splitting (95c6613)
- persistent job queue with background runner and recovery (9e0c53c)
- invoice revisions and document download endpoint (a427a5b)
- CreateInvoice action with snapshots and atomic document pair (0097870)
- invoice number formatting and allocation service (e3ce820)
- deterministic QuestPDF invoice and copy generation (453879b)
- grave management endpoints with links and search (d1ee389)
- invoice item and recipient endpoints (d9b8465)
- OData foundation with settings and VAT rate endpoints (a92ef2f)
- add domain model and initial EF Core migration (ec389cb)

### Fixes
- take the dialog's default recipient from the list the select shows (11f1a6d)
- let the draft own which recipient an invoice goes to (5227c44)
- say what the half-year status means instead of "invalid entry" (2cd7e11)
- divide a grave's net across its recipients, not its gross (5d88768)
- navigate to a sibling grave invoice by RootId instead of its latest revision id (d4def5a)
- return a structured 404 instead of a bare 500 for a deleted grave's invoice group (302157f)
- keep a grave-invoice revision's cent rounding at the recipient it was generated for (35ad2a1)
- stop rounding the grave invoice draft's unit price (5f94df6)
- keep PDF bytes off the EDM and prove per-revision totals in the E2E test (e58d44b)
- don't trust a single sibling for the grave invoice group's prices (14397b4)
- disable crashing HMR middleware (9983eab)
- show skip reasons for grave invoice generation and reject unpriced item links (b3471c2)
- refresh VAT list when the item dialog opens (4780fdd)
- persist settings ending text and move save button into the panel (91c2510)
- vertically center VAT rate row like the stamp word form (4f8a691)
- restore dialog content padding for UI5 1.151 (ca8495b)
- hide revision base number for originals, preserve zero price, german empty-state (3fc8ce8)
- null-body guards on remaining endpoints (2a8da5a)
- merge-blocking fixes from final review (b9eaa41)
- compute build version in the same workflow step (2b78d2f)
- strip version prefix for MSBuild version property (9b037a1)
- show errors in blocking message dialog (fd26828)
- keep two decimals for whole unit prices in the PDF (d9b9629)
- exact cent splits for grave invoices with odd shares (c6fe12c)
- make job test assertion order-independent (b60fa19)
- validate recipient address on invoice revision (b9ca036)
- log internal errors without leaking exception details (72554db)
- invoice numbering year from local time via injected clock (2dbcaf5)
- source remaining document strings from DocumentTexts (1a7acd9)
- unicode-aware case-insensitive grave search (add07fc)
- granular validation in invoice item patch endpoint (50558fb)
- dev container runs as host uid with bind-mounted data dir (123c536)
- set DOTNET_CLI_HOME in dockerized dotnet wrappers (23ef7c1)

### Other changes
- Merge pull request #1 from sebbi08/worktree-release-automation (87fc0ec)
- Merge remote-tracking branch 'origin/main' into worktree-release-automation (1d80009)
- type the detail pages and their shared revision model (6fe70b2)
- bind the import problems panel and type the invoice list (83b8359)
- hold the grave dialogs' state in typed models (db59456)
- bind the half-year and generate selects to a model (06f2955)
- edit settings through a draft model (8ca5a87)
- hold the recipient dialog's state in a model (76852ea)
- hold the item dialog's state in a model, not in its controls (2f990b9)
- describe view state and API payloads as types (e8775da)
- ship one image instead of three (7f6fa3c)
- Revert "wait for a visible suggestion" (aefd36b)
- share the revision-panel markup between the two detail views (7120764)
- hoist the shared view helpers into the base controller (8f6f84e)
- move the grave invoice split into the domain (82507e8)
- Add constitution, feature spec, and Phase 1 design document (b2c1e32)
