# Feature Specification: Invoicing App

**Feature Branch**: `001-invoice-generation`

**Created**: 2026-08-15

**Status**: Current — describes the released application. The design documents
in `docs/superpowers/specs/` record how individual requirements came to read as
they do.

**Input**: User description: "Combine spec 001 (invoice generation & grave management), 002 (data import & export), 003 (invoice copies with diagonal stamp) into one feature specification."

## Clarifications

### Session 2026-08-15

- Q: How is a VAT percentage assigned to an individual invoice line? → A: VAT rate is a fixed property of each invoice item; every invoice line always uses the item's rate.
- Q: When a grave has multiple recipients and the half-year amount is split evenly, what is generated? → A: One invoice per recipient, each containing only that recipient's share; the document states above the item list that it represents only XX% of the total invoice. _(Superseded on 2026-09-01: every recipient's invoice now carries the grave's lines in full and states its share in the VAT block — see FR-037e. Invoices generated before keep their old layout.)_
- Q: Can a generated invoice be edited or deleted afterwards? → A: Not deletable, but editable; each edit saves a new revision while the old version is kept, and the invoice number clearly marks the new revision.
- Q: How are a grave's multiple invoicing items (with counts) and recipients represented in the import CSV files? → A: A separate mapping CSV with grave-id/item-id links, covering both link types (grave↔item with count and grave↔recipient).
- Q: By which identifier does the mapping CSV reference recipients? → A: A recipient number column is added to the recipients CSV; the mapping CSV references it.

## User Scenarios & Testing _(mandatory)_

### User Story 1 - Configure Global Invoice Settings (Priority: P1)

A user defines the VAT percentages, the multi-line ending text, and the copy
stamp word once; they apply to every invoice generated afterwards.

**Why this priority**: Correct VAT, a standard ending text, and a defined stamp
word are required on every invoice; without them no invoice is complete.

**Independent Test**: Can be fully tested by saving VAT rates, an ending text,
and a stamp word, then verifying a newly generated invoice and its copy reflect
exactly those values.

**Acceptance Scenarios**:

1. **Given** no VAT rates are configured, **When** the user saves multiple VAT
   percentages (e.g., 7% and 19%), **Then** those percentages are selectable on
   all subsequent invoices.
2. **Given** the user saves a multi-line ending text, **When** an invoice is
   generated, **Then** the ending text appears exactly as entered, line breaks
   preserved.
3. **Given** no stamp word has been configured, **When** a copy is generated,
   **Then** the word "Kopie" is stamped on it.
4. **Given** the user tries to save an empty stamp word, **When** the setting is
   validated, **Then** the empty value is rejected with a clear message.
5. **Given** the user changes the VAT rates, ending text, or stamp word, **When**
   a new invoice is generated, **Then** only invoices generated after the change
   use the new values; previously generated invoices are unchanged.
6. **Given** a cemetery "Südfriedhof" in the settings, **When** the user adds
   "SÜDFRIEDHOF", **Then** it is refused as already existing; **When** they
   rename "Südfriedhof", **Then** every grave in it shows the new name.
7. **Given** a cemetery that a grave lies in, **When** the user deletes it,
   **Then** the deletion is refused with a clear message; a cemetery without
   graves is deleted.

---

### User Story 2 - Manage Invoice Items (Priority: P1)

A user maintains a catalogue of invoice items, each identified by a number, with
a description, a VAT rate, and an optional default price.

**Why this priority**: Invoice items are the building blocks of every invoice; the
catalogue must exist before invoices can be composed quickly.

**Independent Test**: Can be fully tested by creating and editing an item in the
item management section, then selecting it by number when composing an invoice.

**Acceptance Scenarios**:

1. **Given** the item section is opened, **When** the user creates an item with a
   number, text, a VAT rate, and no price, **Then** the item is saved and
   available for selection by number.
2. **Given** an item has no price, **When** the user selects it on an invoice,
   **Then** the system requires the price to be entered before saving the invoice.
3. **Given** an item has a default price, **When** it is selected on an invoice,
   **Then** the default price is pre-filled and can still be overridden.

---

### User Story 3 - Manage Recipients (Priority: P1)

A user manages recipients (name and address) in a dedicated section and can also
add a recipient on the fly while creating a grave or an invoice.

**Why this priority**: A recipient address is a mandatory invoice field, so
recipients are a prerequisite for any invoice.

**Independent Test**: Can be fully tested by creating, editing, and searching a
recipient in the recipient section, then adding a new recipient during invoice
creation.

**Acceptance Scenarios**:

1. **Given** the recipient section is opened, **When** the user saves a recipient
   with a name and address, **Then** the recipient is stored and listed.
2. **Given** the user is creating a grave or invoice, **When** they add a new
   recipient, **Then** the recipient is created and linked without leaving the
   current form.
3. **Given** a recipient's name or address is changed, **When** an invoice is
   generated later, **Then** the invoice uses the updated values, while previously
   generated invoices keep the values they were issued with.

---

### User Story 4 - Generate a Non-Grave Invoice (Priority: P1)

A user creates an invoice that is not tied to a grave, bound to a single
recipient, containing one or more invoice items selected by number, each with a
count and an optional price. Generation produces a normal invoice and a stamped
copy together.

**Why this priority**: This is the simplest complete invoicing flow and delivers
value immediately, independent of grave management.

**Independent Test**: Can be fully tested by creating an invoice for one recipient
with two items, verifying counts, per-item prices, line totals, VAT, ending text,
and that a normal document and a stamped copy both result.

**Acceptance Scenarios**:

1. **Given** a recipient and invoice items exist, **When** the user creates an
   invoice for the recipient with one or more items (number, count, price), **Then**
   each line shows count, price per item, and a line total equal to count × price.
2. **Given** an invoice contains items with different VAT rates, **When** the user
   finalizes it, **Then** each line's VAT uses its item's rate and the total
   reflects all amounts.
3. **Given** a mandatory field such as the recipient address is missing, **When**
   the user tries to save, **Then** the system blocks saving and points out the
   missing field.
4. **Given** a generated invoice, **When** generation completes, **Then** a normal
   invoice and a stamped copy are both created for it.
5. **Given** a generated non-grave invoice, **When** the user edits and saves it,
   **Then** a new revision is created with a number clearly marking it as a
   revision, and the previous version remains unchanged.

---

### User Story 5 - Manage Graves (Priority: P2)

A user manages graves, each with a grave number, a field number, a site name, a
cemetery, a position, a start month and an optional end month, a creation date, one or more
invoicing items (each with a count and an optional price that
overrides the item's default), and one to four recipients.

**Why this priority**: Graves are the core business object; their configuration is
needed before half-year invoices can be generated.

**Independent Test**: Can be fully tested by creating a grave with a number, field
number, site name, cemetery, position, months, one invoicing item, and two
recipients, then listing it and searching it by column.

**Acceptance Scenarios**:

1. **Given** the grave section is opened, **When** the user saves a grave with a
   number, field number, site name, cemetery, position, start month, one item with count,
   and one to four recipients, **Then** the grave is stored with a creation date
   and is searchable. Saving without an end month is allowed.
2. **Given** the user fills the search of a column of the grave overview,
   **When** they enter a grave number, field number, site name, month, or a
   recipient's name or address, or pick a cemetery, **Then** only matching graves
   are listed.
3. **Given** a new grave that has not been saved yet, **When** the user adds items
   and recipients, **Then** they are stored together with the grave on save.
4. **Given** an item without a default price, **When** the user enters a price for
   it on the grave, **Then** the grave is saved and that price is billed.
5. **Given** a grave with recipients, **When** the user tries to change its
   recipients, **Then** the change is rejected and the grave keeps the
   recipients it was created with.
6. **Given** a grave with number 1234 whose term runs from 01/2020 to 12/2030,
   **When** the user saves another grave with number 1234 and a term that
   overlaps it, **Then** the save is refused, naming the grave that holds the
   number and its term; a term starting 01/2031 is accepted.

---

### User Story 6 - Generate Grave Invoices per Half-Year (Priority: P2)

A user generates invoices for graves for a given half-year. Each recipient gets
their own invoice for the grave's full bill, billed at an identical amount, and
the system records each generation so the same grave cannot be invoiced twice
for the same half-year.

**Why this priority**: Half-year grave invoicing with duplicate protection is the
central recurring business process.

**Independent Test**: Can be fully tested by generating invoices for a half-year,
then verifying that re-generating for the same half-year is blocked and no grave
appears twice.

**Acceptance Scenarios**:

1. **Given** graves with invoicing items and recipients exist, **When** the user
   generates invoices for a selected half-year, **Then** one invoice per recipient
   is created, each with the grave's configured items and counts billed in full,
   at the same amount as every other recipient's invoice.
2. **Given** a grave has multiple recipients, **When** its invoice is generated,
   **Then** each recipient receives their own invoice listing the grave's items in
   full and summarising, beneath them, the grave's net sum and the recipient's
   share of it labelled with the percentage it represents (e.g., 50 % for two,
   33,33 % for three).
3. **Given** an invoice has already been generated for a grave and half-year,
   **When** the user attempts to generate again for the same half-year, **Then** the
   system prevents duplicates and shows that the grave is already invoiced.
4. **Given** the current half-year is active, **When** the grave overview is
   displayed, **Then** graves not yet invoiced for the current half-year are listed
   first.
5. **Given** the user selects a past or future half-year, **When** the view
   updates, **Then** the invoicing status for the selected half-year is displayed.
   The overview picks the year in a calendar (limited to the years in which graves
   exist, plus next year) and the half-year with an H1/H2 toggle.
6. **Given** a grave with two recipients has been invoiced for a half-year,
   **When** the user edits one of those recipients' invoices, **Then** both
   recipients receive an identical new revision of the grave's edited lines,
   and the previous invoices are preserved unchanged.
7. **Given** a grave starting in 05/2026 with an all-year item at 60,00 and a
   Sommer item at 40,00, **When** invoices are generated for H1 2026, **Then**
   the all-year item is billed at 2/6 of its price (20,00, described as
   "anteilig 05–06/2026 (2 von 6 Monaten)") and the Sommer item in full, because
   May and June touch its quarter.
8. **Given** a grave whose term has no month in the selected half-year, **When**
   invoices are generated or the grave overview is displayed for that half-year,
   **Then** no invoice is generated for the grave and it is not listed as open.
9. **Given** the generate dialog, **When** the user picks the year (calendar, any
   future year) and the half-year (H1/H2 toggle), **Then** a half-year more than
   two half-years before the current one cannot be generated: the dialog shows the
   earliest one ("Frühestens H2 2025.") and disables "Jetzt erzeugen" for anything
   older, and the server refuses it too.

---

### User Story 7 - Stamped Copy Appearance (Priority: P2)

The copy carries the configured word as a diagonal stamp across the page, clearly
visible but without hiding the invoice content.

**Why this priority**: The stamp is what makes the copy recognizable at a glance;
it can be visually refined independently of the generation flow.

**Independent Test**: Can be fully tested by opening a generated copy and
verifying the configured word appears diagonally across every page and the
invoice content remains readable.

**Acceptance Scenarios**:

1. **Given** a generated copy, **When** the user opens it, **Then** the configured
   word is stamped diagonally across the page.
2. **Given** a generated copy, **When** the user reads it, **Then** the invoice
   content remains legible despite the stamp.
3. **Given** the stamp word was changed, **When** previously generated copies are
   opened, **Then** they still show the word that was configured when they were
   generated.

---

### User Story 8 - Download Normal Invoices and Copies Separately (Priority: P2)

A user downloads normal invoices and copies via separate actions: normal invoice
downloads never contain copies, and a copy is always downloaded on its own —
there is no bulk download for copies.

**Why this priority**: Keeping the two document kinds separate in downloads
prevents accidentally sending copies where originals are expected.

**Independent Test**: Can be fully tested by bulk-downloading normal invoices and
verifying no copy is included, then downloading a single copy and verifying only
that copy results.

**Acceptance Scenarios**:

1. **Given** generated invoices with copies, **When** the user bulk-downloads
   normal invoices, **Then** only the normal documents are downloaded and no
   copies are included.
2. **Given** generated invoices with copies, **When** the user downloads a copy,
   **Then** only that copy is downloaded; there is no bulk download for copies.
3. **Given** an invoice with a copy, **When** the user downloads the invoice
   document, **Then** only the normal document is downloaded; the copy requires
   its own download action.

---

### User Story 9 - Browse Invoices and Bulk-Download Grave Invoices (Priority: P3)

A user browses the invoices that are not tied to a grave in a list that can be
searched by column, one row per invoice chain, finding them by invoice number,
recipient, date, or total. Grave invoices are browsed through their grave instead; from the grave
overview, a user bulk-downloads the normal documents of a selected half-year's
grave invoices.

**Why this priority**: These conveniences build on the core flows and improve
day-to-day efficiency.

**Independent Test**: Can be fully tested by searching the invoice overview in
each of its four text columns, then selecting a half-year on the grave overview and
bulk-downloading its normal invoices.

**Acceptance Scenarios**:

1. **Given** non-grave invoices with several revisions exist, **When** the user
   opens the invoice overview, **Then** each invoice chain is listed once, showing
   its latest revision.
2. **Given** the user fills the search of a column of the invoice overview,
   **When** they enter an invoice number, a recipient's name or address, a date,
   or a total, **Then** only matching invoices are listed.
3. **Given** a half-year is selected on the grave overview, **When** the user
   triggers the bulk download, **Then** the normal documents of that half-year's
   grave invoices are downloaded, with no copies included.

---

### User Story 10 - Download Import Template (Priority: P2)

A user downloads a template archive containing four CSV files — recipients, items,
graves, and a mapping file linking graves to items and recipients — each with
header rows, so they can prepare their data offline.

**Why this priority**: Without the template, users cannot know the expected
structure, and incorrect files would be the norm.

**Independent Test**: Can be fully tested by downloading the archive, opening the
four CSV files, and verifying each contains its expected header row.

**Acceptance Scenarios**:

1. **Given** the user opens the import/export section, **When** they trigger the
   template download, **Then** a ZIP archive is downloaded containing exactly
   four CSV files with header rows: recipients, invoice items, graves, and the
   mapping file.
2. **Given** a fresh installation with no data, **When** the user downloads the
   template, **Then** the template still downloads successfully with all headers.

---

### User Story 11 - Validate Archive Before Import (Priority: P2)

A user selects a prepared ZIP archive. The system checks the archive and all of
its contents before importing anything, and reports every problem with enough
detail to locate it.

**Why this priority**: Pre-import checking protects existing data and prevents
half-imported, inconsistent datasets.

**Independent Test**: Can be fully tested by uploading an archive with known
defects and verifying a clear error report is shown while no data is changed.

**Acceptance Scenarios**:

1. **Given** a ZIP that is missing one of the four CSV files, **When** the user
   tries to import, **Then** the system reports the missing file and imports
   nothing.
2. **Given** a CSV with a wrong or missing header row, **When** the user tries to
   import, **Then** the system reports the file and the problem and imports
   nothing.
3. **Given** a CSV row with an invalid or empty required field, **When** the user
   tries to import, **Then** the system reports the file, the row, and the reason,
   and imports nothing.
4. **Given** the validation finds no errors, **When** the import proceeds, **Then**
   all data from the archive is imported.

---

### User Story 12 - Import Recipients, Items, and Graves Consistently (Priority: P2)

A user imports a valid archive. Recipients and invoice items from their files are
created, and every grave's invoicing items and recipients resolve to entries from
the other files via the mapping CSV.

**Why this priority**: This transfers configuration data into the application in
one step with total consistency.

**Independent Test**: Can be fully tested by importing an archive with recipients,
items, graves, and a mapping file that reference each other, then verifying all
cross-references exist and no data was lost.

**Acceptance Scenarios**:

1. **Given** a valid archive with recipients, items, graves, and mappings, **When**
   the import completes, **Then** all recipients, items, and graves from the
   archive exist in the application.
2. **Given** the mapping CSV references an invoice item or recipient that is not
   defined in the items or recipients CSV file, **When** the user tries to import,
   **Then** the system reports the inconsistency (file and row) and imports nothing.
3. **Given** the mapping CSV links a grave to an item defined in the items file,
   **When** the import completes, **Then** the grave's invoicing items use the
   imported item's number, text, VAT rate, and price.

---

### User Story 13 - Blocking Progress During Long Tasks (Priority: P2)

While a long-running task (large import or batch invoice generation) runs, the
user sees a progress indicator and the interface is blocked until the task
finishes, so it cannot be started twice.

**Why this priority**: Blocking progress feedback prevents duplicate imports,
duplicate generations, and data inconsistency.

**Independent Test**: Can be fully tested by starting a large import or batch
generation and verifying a progress indicator appears, the interface is
non-interactive until completion, and the task runs exactly once.

**Acceptance Scenarios**:

1. **Given** the user starts an import or batch generation, **When** the task is
   running, **Then** a progress indicator is visible and interaction with the
   affected interface is blocked.
2. **Given** a task is running, **When** the user attempts to start another,
   **Then** the second attempt is prevented until the first completes.
3. **Given** the task finishes, **When** the indicator closes, **Then** the
   interface is interactive again and the result is shown.

---

### Edge Cases

- What happens when an invoice item without a price is used and no price is
  entered? The system must not save the invoice until a price is provided.
- What happens when a grave has no recipients? Invoice generation must be blocked
  with a clear message.
- What happens when an invoice is generated twice for the same grave and
  half-year? The second attempt must be prevented.
- How are rounding differences handled when a grave's per-rate subtotal does not
  divide evenly among its recipients? One line per invoice of the term and VAT
  rate is raised by the smallest amount, at most 1,00 €, at which the subtotal
  divides; the grave's editor shows it, the user confirms it before saving, and
  the correction is stored with the grave. A price is never lowered. Only when
  no line can be raised far enough is saving refused, naming the first invoice
  that does not divide.
- What happens when a recipient's address is changed after invoices were
  generated? Historical invoices must remain unchanged.
- What happens when a grave's end month lies before the selected half-year (or
  its start month after it)? The half-year has no month inside the term, so no
  invoice is generated and the grave is not listed as open for it, unless it was
  invoiced for that half-year before this rule existed.
- What happens when a grave has no end month? Its term runs indefinitely, so no
  other grave may take its number from its start month on.
- What happens when a generated invoice is edited? The previous version is kept
  unchanged and the new document is saved as a revision with a clearly marked
  number.
- What happens when the recipient invoices of a grave and half-year no longer
  carry the same lines? The edit is refused with a clear message instead of
  picking one of them.
- What happens when the stamp word is empty? Saving the configuration must be
  rejected.
- What happens when the stamp word is very long? The stamp must still fit on the
  page without breaking the layout.
- What happens with multi-page invoices? The stamp appears on every page of the
  copy.
- What happens when the copy generation succeeds but the normal generation fails
  (or vice versa)? No half-generated pair may be persisted.
- What happens when the archive contains more files than the four expected ones?
  Extra files are ignored without error.
- What happens when a CSV is empty apart from the header row? It is treated as
  "no data" and the import continues with the other files.
- What happens when the same number appears twice within the items file, the
  recipients file, or the graves file? Each duplicate is reported with its rows
  and the import is rejected.
- What happens when imported data conflicts with data that already exists in the
  application? The import is rejected for the conflicting rows and nothing is
  imported.
- What happens when the mapping CSV links the same grave and recipient twice or
  the same grave and item twice? The duplicate link is reported and the import is
  rejected.
- What happens when the import is interrupted (e.g., connection loss)? No partial
  data remains; the import can be repeated from scratch.
- What happens when a search returns no matches? A clear empty state is shown.
- What happens when a cemetery that graves lie in is deleted? The deletion is
  refused; the graves have to be moved to another cemetery first.
- What happens when an archive names a cemetery the settings do not have? The
  row is reported and nothing is imported; the cemetery has to be added in the
  settings first.

## Requirements _(mandatory)_

### Functional Requirements

**Global configuration**

- **FR-001**: The system MUST allow configuring multiple VAT percentages globally.
- **FR-002**: The system MUST allow configuring a multi-line ending text globally.
- **FR-002a**: Users MUST be able to keep the cemeteries graves lie in, in the
  settings: list, add, rename and delete them. A cemetery has a name, required
  and trimmed, which MUST be unique ignoring case, umlauts included ("Südfriedhof"
  and "SÜDFRIEDHOF" are one name). Graves refer to their cemetery, so a rename
  MUST apply to every grave in it at once. Deleting a cemetery MUST be refused,
  with a clear message, while any grave lies in it.
- **FR-003**: Invoices MUST be generated from a template.
- **FR-004**: Invoices MUST include mandatory fields, including the recipient
  address, and MUST block generation when a mandatory field is missing.
- **FR-005**: The copy stamp word MUST be globally configurable, MUST default to
  "Kopie", and MUST NOT be empty.

**Invoice items**

- **FR-006**: Each invoice item MUST have a number, a text, and a VAT rate chosen
  from the globally configured VAT percentages.
- **FR-007**: An invoice item's price MUST be optional.
- **FR-008**: When an invoice item has no configured price, the user MUST provide
  a price when the item is used.
- **FR-009**: Users MUST be able to create and edit invoice items in a dedicated
  section.
- **FR-009a**: An invoice item's VAT rate MUST NOT change once a grave bills the
  item: the rate is billed per grave, and a change would move every linked
  grave's split at once. The same holds for the item's quarter (FR-019), which
  moves the invoices that carry the item.
- **FR-009b**: A changed default price MUST apply only to graves linked from then
  on. A grave that was billing the old default MUST keep it, written onto the
  link as its own price; a link price equal to the default counts as billing the
  default. The item section MUST show which graves bill an item, grouped by the
  price they bill.
- **FR-010**: Users MUST be able to select invoice items by their number when
  composing invoices.
- **FR-010a**: Where an item is picked, users MUST be able to search invoice
  items by number and description.
- **FR-010b**: The item list MUST offer a search per column (FR-068c): text
  fields for the number, the description and the default price (as the list
  shows it), and selects for the VAT rate (among the configured rates) and the
  quarter (all, whole year, or one of the quarters by the name the list shows,
  such as "Frühling" for spring).

**Invoice lines and totals**

- **FR-011**: Each invoice line MUST contain a count, a price per item, and a line
  total.
- **FR-012**: The system MUST calculate each line total as count × price per item.
- **FR-013**: The system MUST apply each invoice item's VAT rate to its line
  total.

**Recipients**

- **FR-014**: A recipient MUST have a name and an address, and MAY have an
  e-mail address; one that is given MUST look like an address (one "@" with
  text on both sides, a dot in the domain, no whitespace).
- **FR-015**: Users MUST be able to manage recipients in a separate section.
- **FR-016**: Users MUST be able to add recipients while creating a grave or an
  invoice.
- **FR-016a**: Where a recipient is picked, the list MUST offer the 20 newest
  recipients and search all of them by name, number and address, never loading
  every recipient. It MUST say so when a search leaves more than it shows. A
  recipient the grave being edited already has MUST NOT be offered again.
- **FR-016b**: The recipient picker's search MUST look in the name, the number
  and the address.
- **FR-016c**: The recipient list MUST offer a text search per column (FR-068c):
  number, name, address (all its lines) and e-mail. It MUST be answered by the
  server, which pages the list.
- **FR-017**: Changes to a recipient's name or address MUST only apply to future
  invoices; previously generated invoices MUST keep the values they were issued
  with.

**Graves**

- **FR-018**: A grave MUST have a grave number, a field number, a site name, a
  cemetery, a position (double, single or urn grave, inside or outside) and a
  start month; the end month is optional. The site name holds the site's own
  name only; the cemetery is a field of its own. A grave MAY be marked as a
  cooperative grave (Genossenschaftsgrab): the cooperative looks after it, so it
  is kept and listed here but never invoiced. A cooperative grave MUST carry a
  contract number (Vertragsnummer), stored trimmed; saving without one MUST be
  refused, and a grave that is not cooperative MUST store none. A grave that has
  invoices MUST NOT have the mark ticked or unticked.
- **FR-018a**: Two graves MUST NOT share a grave number while their terms overlap;
  a term runs from its start month through its end month, or indefinitely without
  one. Field numbers may repeat.
- **FR-018b**: Graves imported from the old program MAY carry further positions:
  single, double, urn, triple, quadruple, child, lawn, lawn urn, tree, honorary
  and slab graves inside, outside or with the side unknown, and graves whose kind
  is unknown. The editor MUST NOT offer them for a grave; a grave that has one
  MUST keep it when it is saved unchanged.
- **FR-018c**: A grave's cemetery MUST be one of those kept in the settings
  (FR-002a). The grave editor MUST offer them all in a field that can be searched,
  and MUST NOT save a grave without one; saving MUST be refused for a cemetery
  that does not exist. The cemetery MUST NOT be printed on invoices, and
  invoices MUST NOT keep a copy of it.
- **FR-018d**: A grave MAY be handed over between the cooperative and the
  business from a month on: a new grave with the same grave number, the opposite
  cooperative mark and a start month after the old grave's start takes over, and
  the old grave then ends the month before, in the same save. Saving MUST be
  refused, leaving the old grave unchanged, when the two have the same mark, when
  the numbers differ, when the old grave has invoices for a half-year that
  reaches the new start month, or when ending it earlier would change the rounding
  corrections stored for it. A successor that is cooperative MUST carry a
  contract number, and saving without one MUST be refused before the old grave
  is changed. Its earlier invoices MUST stay as they are.
- **FR-018e**: A grave MAY be deleted. Deleting marks it with the time and keeps
  it and its invoices; a deleted grave MUST NOT be saved or handed over, and MUST
  NOT count against its grave number, so the number may be given to a new grave
  with an overlapping term. A deleted grave MAY be restored until an invoice run
  for graves has finished successfully since it was deleted, whatever half-year
  that run was for; restoring MUST be refused after that, and when its number has
  meanwhile been taken by a grave whose term overlaps. Deleting a deleted grave
  and restoring one that is not deleted MUST be refused, as both MUST be while an
  invoice run for graves is active. Deleting MUST always ask for confirmation,
  spelling out the consequences: the grave leaves every list and statistic except
  "Inaktive Gräber", no invoices are generated for it any more, its invoices no
  longer count and can be neither paid nor changed, and it can be restored only
  until the next run. The detail page of a deleted grave MUST show that it is
  deleted, with the day of deletion (in the local calendar), and "Wiederherstellen"
  while the window is open or else the sentence saying why it can no longer be
  restored; it MUST offer neither editing nor handing over, show no billing
  preview, and show the grave's invoices read-only, as does the grave invoice page.
- **FR-019**: A grave MUST support one or more invoicing items, each with a count
  and an optional price that overrides the item's default price for that grave.
  An item is billable for a grave when it has either of the two prices. An item
  MAY carry one quarter: Frühling (January–March), Sommer (April–June), Herbst
  (July–September) or Winter (October–December); without one it is billed all
  year. An all-year item's price is per half-year, and in a half-year the term
  covers only partly (the first and the last) it MUST be billed at its price
  times the covered months divided by 6, rounded to the cent, with the covered
  months appended to the line's description; the start and end month each count
  as a whole month and the count is unchanged. A quarter item MUST be billed in
  full, once, in the half-year that holds its quarter, when the invoice's months
  touch that quarter at all, and not otherwise. A grave MUST have at least one
  item billed all year (without a quarter). A cooperative grave is exempt: it
  MAY have no item at all, an item without a price, and no all-year item.
- **FR-020**: A grave MUST support one to four recipients. A cooperative grave
  MAY have none, and at most four. An ordinary grave imported without recipients
  (FR-058a) stays saveable without them until it is given some; generation
  skips it until then.
- **FR-020a**: Users MUST be able to add a grave's items and recipients before the
  grave is saved; the grave and its links MUST be stored together in one step.
- **FR-020b**: A grave's set of recipients MAY change until the grave has its
  first invoice and MUST be fixed from then on. Unticking a cooperative grave
  applies every rule of an ordinary grave, including FR-020 and FR-021a.
- **FR-021**: When a grave has multiple recipients, the system MUST generate one
  invoice per recipient, each carrying the grave's own items and counts in full,
  billed at the same amount as every other recipient's invoice.
- **FR-021a**: The grave's per-VAT-rate subtotals MUST divide evenly across its
  recipients so that every recipient's share is identical, and every invoice's
  VAT MUST follow from its own share of that net. This MUST hold for every
  invoice the term produces — the prorated first and last half-year as well as
  a full one — not only for a full half-year. Where a subtotal does not divide,
  the system MUST correct it instead of refusing the grave: per invoice of the
  term and VAT rate it MUST raise one line, the first all-year line of that
  rate (otherwise the first line of the rate, and the next line of the rate if
  that one cannot fix it), by the smallest amount at which the subtotal
  divides, and by at most 1,00 €. A price MUST NOT be lowered. A save that needs
  corrections MUST first list them (invoice, item, VAT rate, entered and
  corrected price) in a confirmation the user can cancel, and the corrections
  MUST be stored with the grave and applied when its invoices are generated.
  Only when no line can be raised by 1,00 € or less MUST saving be refused,
  naming the first failing invoice, the VAT rate and its subtotal. The grave's
  editor MUST show the example invoices, their check and each corrected price
  (marked as adjusted, with the price as entered) while the user types. Every
  grave MUST have an item billed all year, which carries the correction. Where
  a rate carries a price entered as a gross, what the recipients collect between
  them MUST come to exactly that gross, and the save MUST be refused otherwise.
  The start block of a term MUST be checked and corrected whenever its
  half-year can still be generated — the current half-year and up to two back,
  or a later one. Only the start of a term in an older half-year is left out,
  because it can no longer be invoiced.
- **FR-022**: Each recipient invoice MUST state which percentage of the grave's
  bill it represents, in its summary beneath the item list: the grave's net sum,
  then the recipient's share of it labelled with that percentage, then the VAT
  worked out per rate from that share. The percentage MUST be shown to at most
  two decimals, so three recipients each read 33,33 %.
- **FR-023**: The system MUST record a creation date for each grave.
- **FR-024**: The grave overview MUST offer a search per column (FR-068c): text
  fields for the grave number, the site name, the field number, the start and
  end month (as shown, `MM/yyyy`) and the recipients, a select for the
  cemetery (among those in the settings), and selects for the invoicing status
  (open or invoiced) and the payment status (FR-077). The recipients search
  MUST look in the names and addresses of all of the grave's recipients. The
  list is filtered in the browser, folding case including umlauts. The overview
  lists the graves the business bills: a cooperative grave MUST NOT appear in
  it, only in the list of its own (FR-024a); nor MUST a deleted grave (FR-018e),
  even one that was invoiced before it was deleted. The graves an item bills
  (the item dialog) leave deleted graves out as well.

**Half-year grave invoicing**

- **FR-025**: Grave invoices MUST be generated per half-year period, and only for
  a half-year with at least one month inside the grave's term. Before a run the
  system MUST recompute the rounding corrections (FR-021a) of every grave it
  would invoice and stop the whole run, naming those graves, when a stored
  correction differs from the current one; such a grave has to be opened and
  saved again first. This is a safeguard against the rule or the stored data
  changing underneath a stored correction (for instance after an update).
  A cooperative grave is never invoiced: it is not due in any run, is left out of
  that check, and the generator skips it with the reason `Cooperative`.
  A deleted grave (FR-018e) is treated the same way: not due in any run, left out
  of that check, skipped by the generator with the reason `Deleted`. A run with
  nothing due but deleted graves is refused like a half-year that is already
  invoiced.
  A run MUST NOT reach further back than two half-years before the current one
  and MUST refuse an older half-year, naming the earliest one allowed.
- **FR-026**: The system MUST record that a grave's invoices have been generated
  for a given half-year.
- **FR-027**: The system MUST prevent generating duplicate invoices for the same
  grave and the same half-year.
- **FR-028**: The grave overview MUST list graves not yet invoiced for the current
  half-year first. A grave whose cooperative flag was unticked is listed like any
  other, as open until it is invoiced.
- **FR-024a**: The system MUST offer a list of the cooperative graves of its own,
  with each grave's number, field, site name, cemetery, contract number,
  recipients and term, ordered by grave number, from which a grave opens in the editor and a new
  cooperative grave can be created. The list offers the search per column of
  FR-024 and lists only current and future graves: those with no end, or an end
  in the current half-year or later, and not deleted (FR-018e); ended and deleted
  graves are kept out of it. A grave's detail page
  opened from this list MUST keep the list marked in the menu and lead back to it
  through its breadcrumb, as one opened from the grave overview does for that.
- **FR-024b**: The system MUST offer a page "Inaktive Gräber" with four panels:
  "Beendete Gräber", "Gelöschte Gräber", "Beendete Genossenschaftsgräber" and
  "Gelöschte Genossenschaftsgräber". A grave is listed in exactly one panel or
  none: a deleted grave (FR-018e) in the panel of deleted graves, whether it has
  ended or not; a grave that is not deleted and whose end lies before the first
  month of the current half-year in the panel of ended graves; each in the
  cooperative panel when the grave is cooperative. Each panel shows number, site
  name, cemetery, term (and for cooperative graves the contract number, for deleted
  ones the day of deletion) and offers a text search of its own. A grave opens
  from it in its detail page, which MUST keep the page marked in the menu and lead
  back to it through its breadcrumb.
- **FR-029**: Users MUST be able to select a half-year to view graves invoiced in
  past or future half-years.
- **FR-029a**: The grave overview MUST show each grave's cemetery, as it shows
  its site name, and the grave's detail page MUST name it.
- **FR-029b**: The grave overview MUST show each grave's recipients in a column
  of their own: the name of a grave's only recipient, and for two to four
  recipients a button "{n} Empfänger" that opens a popover with all their names,
  one per line.

**Non-grave invoices**

- **FR-030**: Users MUST be able to generate invoices that are not tied to a grave.
- **FR-031**: A non-grave invoice MUST be bound to exactly one recipient.
- **FR-032**: A non-grave invoice MUST support one or more items, selected by
  number, each with a count and an optional price.

**Invoice revisions**

- **FR-033**: Users MUST be able to edit a generated invoice. A grave invoice
  MUST NOT be edited on its own; it MUST be edited only through its grave (see
  FR-034a).
- **FR-034**: Editing a non-grave invoice MUST create a new revision; the
  previously generated version MUST be preserved unchanged.
- **FR-034a**: Editing a grave invoice MUST create a new revision for every
  recipient of that grave and half-year, billing each of them the edited lines
  in full, the same way generation bills them.
- **FR-034b**: The edit MUST start from the amounts as they were invoiced for
  that half-year, not from the grave's current configuration.
- **FR-035**: The invoice number of a revision MUST clearly indicate that it is a
  new revision of an existing invoice.
- **FR-036**: Invoices MUST NOT be deletable.

**Documents and copies**

- **FR-037**: The system MUST generate, for every invoice (grave and non-grave),
  a normal invoice document and a copy document in the same generation step.
- **FR-037a**: The head of an invoice document MUST show, beside the recipient's
  address, the invoice number, the customer number (the recipient's number), the
  grave number as "Laufweg-Nr." on grave invoices only, the date and the page
  ("1 von 2"). A value that is not known for an invoice MUST be left out.
  Continuation pages MUST carry a single line with the invoice number, the
  customer number and the page.
- **FR-037b**: A grave invoice MUST show, in bold above its lines, the site name
  ("GRABSTÄTTE") and the field number with the position's short form
  ("FELD-GRAB-NR", e.g. "27 dGr.a"; kinds without an abbreviation are spelled
  out, "27 Rasengrab außen"). A line whose values are missing MUST be left out.
- **FR-037c**: The line table MUST have the columns item number, description,
  count, unit price, total and VAT rate ("19 %"). A line whose price includes VAT
  MUST read "7 % Incl." and print the gross unit price and gross total.
- **FR-037d**: The ending text MUST follow the lines. At the foot of the last page
  a VAT block MUST show, per VAT rate, the net and the VAT, followed by the net
  amount, the VAT and the gross amount. The block MUST NOT be split across pages.
- **FR-037e**: On a grave invoice with several recipients the VAT block MUST bill
  the recipient's share, and one line above it MUST give the net sum of the whole
  grave and the recipient's percentage; the lines themselves stay in full.
- **FR-038**: The copy document MUST carry the configured stamp word diagonally
  across the page.
- **FR-039**: The stamp MUST appear on every page of a multi-page copy.
- **FR-040**: The stamp MUST be clearly visible while keeping the invoice content
  legible.
- **FR-041**: A change to the stamp word MUST apply only to copies generated
  after the change; existing copies MUST remain unchanged.
- **FR-042**: The normal invoice and the copy MUST be distinguishable in the user
  interface.
- **FR-043**: Generating the document pair MUST be all-or-nothing: either both
  documents are created completely or neither is.
- **FR-044**: Both grave invoices and non-grave invoices MUST receive the same
  normal-plus-copy treatment.

**Downloads**

- **FR-045**: Users MUST be able to bulk-download the normal documents of the
  grave invoices of a selected half-year, leaving out the invoices of deleted
  graves (FR-018e).
- **FR-046**: Bulk download of normal invoices MUST NOT include copies.
- **FR-047**: A copy MUST be downloadable individually wherever an invoice is
  listed or shown; copies MUST NOT be downloadable in bulk.
- **FR-048**: Downloading a single invoice MUST deliver only the normal document;
  the copy MUST be downloadable via its own action.

**Import and export**

- **FR-049**: The system MUST provide a downloadable ZIP archive containing
  exactly four CSV files with header rows: recipients, invoice items, graves, and
  a mapping file.
- **FR-050**: The recipients CSV MUST capture a recipient number, a name, an
  address and an optional e-mail address. A file without the `Email` column
  (header included) is read as having no e-mail addresses.
- **FR-051**: The items CSV MUST capture item number, text, VAT rate, an
  optional price and an optional quarter, in that order. The quarter is written
  as `Spring`, `Summer`, `Autumn` or `Winter` or left empty; any other value is
  a problem. An archive whose items CSV lacks the `Quarter` column is refused.
- **FR-052**: The graves CSV MUST capture the grave number, field number, site
  name, cemetery, position, start month and an optional end month, in seven
  columns, plus an eighth marking a cooperative grave (FR-020) and a ninth with
  its contract number:
  `GraveNumber,FieldNumber,SiteName,Cemetery,Position,StartDate,EndDate,Cooperative,ContractNumber`.
  `Cooperative` is read like the mapping's VAT-included flag (`true`/`false`,
  `ja`/`nein`, `1`/`0`; empty is false); a value that is none of these is a
  problem naming the row and the value. `ContractNumber` is required for a
  cooperative grave (a row without one is a problem naming the grave) and
  ignored for any other. Either or both of the last two columns may be left out
  of the file with their headers, as `Email` and `UnitPrice` may: a file without
  them reads as ordinary graves.
  Months are written as YYYY-MM. The cemetery is required and names one kept in
  the settings (FR-002a), matched ignoring case; one that is not there is a
  problem naming the row and the value. The import MUST NOT create cemeteries.
  An archive whose graves CSV lacks the `Cemetery` column is refused.
- **FR-053**: The mapping CSV MUST link each grave to its invoicing items (item
  identifier with count, VAT-included flag and an optional unit price for this
  grave) and to its recipients (recipient number, the other three columns
  empty), covering both link types. A link's unit price is billed instead of the
  item's price, exactly as one set in the grave editor; a file without the
  `UnitPrice` column (header included) bills every item at its own price.
- **FR-054**: The system MUST validate an archive before importing any data.
- **FR-055**: Validation MUST check that all four CSV files are present.
- **FR-056**: Validation MUST check that each CSV file has a correct header row.
- **FR-057**: Validation MUST check each row for missing or invalid required
  fields.
- **FR-058**: Validation MUST check cross-file consistency: every invoice item
  and recipient referenced in the mapping CSV MUST be defined in the items or
  recipients CSV file.
- **FR-058a**: Validation MUST refuse a grave that could not be billed: a link
  to an item without a price of its own or on the link, or prices that do not divide across the grave's recipients even after a
  rounding correction (FR-021a; the first failing invoice of a grave is reported
  with its label), a grave with item links but none billed all year, or more
  than four recipients (FR-020). A grave without recipient links, or without
  item links, imports: it is completed in the editor before its first invoice,
  and the checks that need what is missing (the split across recipients, the
  all-year item) wait until it is there.
  A grave marked cooperative in its `Cooperative` column is exempt from all of
  these but the cap of four recipients: it needs no recipient, no all-year item and no prices, and stores
  no rounding corrections.
  A price that merely needs a correction is accepted; the correction is stored
  with the grave without a confirmation. The archive is refused as a
  whole, so nothing imports that the dialog would have refused to save.
- **FR-059**: Validation MUST report each problem with the affected file, the row,
  and a human-readable reason.
- **FR-060**: The system MUST NOT import any data when validation reports one or
  more errors (all-or-nothing).
- **FR-061**: The system MUST create recipients, invoice items, and graves from a
  valid archive, linking each grave's items and recipients via the mapping CSV.
- **FR-062**: After a successful import, the system MUST show a summary of the
  imported data (counts per file) and, when graves needed rounding corrections,
  how many ("Gräber mit Rundungsanpassungen: {n}").

**Progress and language**

- **FR-063**: During a long-running task (import or batch generation), the user
  interface MUST show a progress indicator and MUST block interaction with the
  affected interface until the task completes.
- **FR-064**: All user-visible text — the user interface and the generated
  documents — MUST be German.

**Invoice lists and detail pages**

- **FR-065**: A bulk download MUST contain only the latest revision of each
  invoice.
- **FR-066**: Invoice lists MUST show the latest revision of each invoice;
  earlier revisions MUST remain visible on the invoice's detail page.
- **FR-067**: The system MUST provide detail pages for an invoice, a grave, and
  a grave invoice, each showing every revision of the invoices it covers.
- **FR-068**: The invoice list MUST offer a text search per column (FR-068c): the
  invoice number (a search for the base number finds every revision), the
  recipient (name and address), the invoice date (as shown, `dd.MM.yyyy`) and
  the invoice total (gross; matched as an amount, FR-068b).
  These searches MUST be answered by the server, which pages the list.
- **FR-068a**: The invoice list, the grave overview, the list of cooperative
  graves, the recipient list and the item list MUST show 10 entries per page.
  The pages MUST be cut from the list as filtered, and a changed column search
  MUST start on the first page. Every other table that lists records (the
  panels of "Inaktive Gräber", the VAT rates and cemeteries in the settings, a
  grave's invoices and an import's problems) MUST show 10 entries per page as
  well, its paginator appearing once there is a second page; a panel's search
  starts on its first page.
  The paginator MUST stay visible at the lower edge of the window while the
  rows of a page scroll.
- **FR-068b**: Every search (each column's search in the four lists, and the
  item and recipient pickers) MUST take its term as words separated by
  whitespace and leave the entries that hold every one of them, in any order and
  ignoring case. The four lists' column searches MUST ignore the case of umlauts
  too ("ö" finds "Ö"); the pickers fold the case of A–Z only. The words MAY be
  found in different fields of those the search looks in: "Hanna Bauer" MUST
  find a recipient named "Bauer Hanna", and a name typed together with a street
  the recipient who lives there. A search for an amount (the invoice list's
  total, the item list's price) MUST find it as shown ("1.234,56 €"), without
  the "€", without the thousands separator ("1234,56") and with a point for the
  decimal comma ("1234.56", "12.50"); "12,5" MUST find 12,50.
- **FR-068c**: The invoice list, the grave overview, the item list and the
  recipient list MUST each offer a search per column instead of a single search
  field. Every column that holds data has its own: a text field, or a select
  for a column of fixed values, "Alle" being its empty choice; columns that hold
  only buttons have none. All searches that are filled MUST apply together.
  On a screen wide enough to show every column of the list (the grave overview
  from 1536 px, so Beginn and Ende stay hidden below it), the searches MUST sit
  in a row directly under the column headers. On a narrower screen the list
  MUST show a "Filter" button with the number of active searches instead, which
  opens a sheet from the bottom with a labelled field for every searchable
  column, hidden columns included, and a button to clear them all. The active
  searches MUST also show as chips above the list, each removable on its own.
  The row and the sheet MUST edit the same searches, applied as the user types.
- **FR-068d**: The column searches MUST live in the URL query of the list, so
  that leaving for a detail page and coming back, reloading, or sharing the
  link shows the same filtered list; a breadcrumb back to the list MUST lead to
  it as it was left. On the grave overview the selected
  half-year MUST live in the URL query too, though it is no column search:
  clearing the searches leaves it alone.

**Payment status**

- **FR-069**: Users MUST be able to mark any invoice, including a grave invoice,
  as paid, and to take that mark back.
- **FR-070**: The system MUST record the day an invoice was marked paid. The user
  interface MUST show only whether an invoice is paid, never that day.
- **FR-071**: Marking an invoice paid MUST apply to the individual invoice. Each
  recipient of a grave's half-year MUST be markable independently. The
  invoices of a deleted grave (FR-018e) MUST NOT be marked paid, nor taken back
  as unpaid, nor revised: the system MUST refuse with `GraveDeleted` until the
  grave is restored.
- **FR-072**: Taking a payment mark back MUST require a confirmation.
- **FR-073**: The system MUST refuse to revise an invoice that is marked paid.
- **FR-074**: The system MUST refuse to edit a grave invoice while any recipient
  of that grave and half-year is marked paid, and MUST name those recipients.
- **FR-075**: Only the latest revision of an invoice MUST be markable as paid.
- **FR-076**: The invoice list MUST offer a payment status search (FR-068c): all
  invoices, only unpaid ones, or only paid ones.
- **FR-077**: The grave overview MUST show, for the selected half-year, whether a
  grave's invoices are unpaid, partly paid or paid; for a half-year with no
  invoice at all, this MUST show nothing, since the invoicing-status column
  beside it already reports that. The overview's payment status search
  (FR-068c) MUST be able to narrow the list to the graves that still have an
  unpaid invoice in that half-year.

### Key Entities _(include if feature involves data)_

- **Recipient**: A person or organization; has a recipient number, a name, and an
  address. Each generated invoice captures the recipient values used at the time
  of generation.
- **Invoice Item**: A catalogue entry identified by a number, with a text, a VAT
  rate, an optional default price and an optional quarter.
- **Grave**: Identified by a grave number (unique among graves whose terms
  overlap); has a field number, a site name, a cemetery, a position, a start month, an
  optional end month, a creation date, one or more invoicing items (with counts), and one to four recipients,
  fixed once the grave is created.
- **Invoice**: A generated document; tied either to a grave (for a half-year, one
  invoice per recipient carrying the grave's own lines in full, at the same
  amount for every recipient, together with a percentage note stating their
  share) or to a single recipient (non-grave). Carries invoice lines, VAT,
  ending text, generation date, and a unique invoice number. It snapshots the
  recipient's number and, on a grave invoice, the grave's number, site name, field
  number and position, as it does the recipient's name and address. Edits create new
  revisions; previous revisions are preserved and invoices cannot be deleted.
- **Invoice Line**: A single item on an invoice; has a count, a price per item,
  and a line total. On a grave invoice generated before recipients were billed
  the grave's lines directly, the stored price is one recipient's share, and
  the line also carries the price it was split from before that split; both
  are null on non-grave invoices and on grave invoices billed in the grave's
  own lines directly. Where that pre-split price is missing, the grave's line
  is instead reconstructed from the recipients' shares — a best-effort
  calculation that can be a cent or two off for very small amounts.
- **Invoice Document**: The generated PDF for an invoice revision; each revision
  has two documents — a normal document and a stamped copy.
- **VAT Rate**: A globally configured percentage, assigned to invoice items and
  applied to their line totals.
- **Global Settings**: Holds the VAT rates, the multi-line ending text, and the
  copy stamp word (default "Kopie").
- **Cemetery**: Kept in the settings; has a name, unique ignoring case. Graves
  refer to it; it is not part of any invoice.
- **Import Template**: A ZIP archive with four CSV files (recipients, items,
  graves, mapping), each with a header row; downloadable even when no data exists.
- **Validation Report**: A list of problems found before import, each locating the
  file, row, and reason.

## Success Criteria _(mandatory)_

### Measurable Outcomes

- **SC-001**: A user can configure VAT rates, the ending text, and the stamp word,
  then generate a first complete invoice in under 10 minutes.
- **SC-002**: 100% of generated invoices show correct line totals (count × price
  per item) and correctly applied per-item VAT rates.
- **SC-003**: Generating grave invoices for a half-year yields exactly one invoice
  per recipient, with zero duplicates across a batch of 50 or more graves.
- **SC-004**: For graves with multiple recipients, every recipient is billed an
  identical amount, and their shares sum exactly to the net sum printed on the
  invoice. Prices that do not split that way are corrected
  (one price per block and VAT rate, by at most 1,00 €, confirmed on saving);
  a grave no correction can fix cannot be saved.
- **SC-005**: A user can locate a specific grave by number or recipient name within
  30 seconds.
- **SC-006**: A bulk download of N normal grave invoices for a half-year yields
  exactly N documents, never a copy and never a superseded revision.
- **SC-007**: Changing a recipient's name or address never alters previously
  generated invoices (100% immutability of historical invoices).
- **SC-008**: 100% of invoice edits preserve the previous version and produce a
  new revision whose number clearly marks it as a revision.
- **SC-009**: 100% of generated invoices produce exactly two documents: a normal
  invoice and a stamped copy.
- **SC-010**: 100% of copies show the stamp word configured at the time of
  generation, diagonally across every page, with invoice content still legible.
- **SC-011**: Changing the stamp word affects only future copies; 100% of existing
  documents remain unchanged.
- **SC-012**: Every copy is downloadable individually from the detail pages of
  the invoice it belongs to.
- **SC-013**: Users can tell the normal invoice apart from the copy at a glance
  without opening both documents.
- **SC-014**: A user can download the import template, fill it, and prepare a
  valid import archive in under 5 minutes without additional instructions.
- **SC-015**: 100% of archives with structural or content errors are rejected
  before import with an error report naming the file and row of every problem, and
  zero data is imported.
- **SC-016**: A valid archive containing 50 recipients, 20 invoice items, and 100
  graves imports completely, with all grave references resolving to the imported
  items and recipients (100% consistency).
- **SC-017**: After an import, every grave's invoicing items and recipients can be
  traced back to an entry in the imported items or recipients data.
- **SC-018**: An import or batch generation of 100 or more graves runs with a
  visible progress indicator, runs exactly once, and leaves no partial data.
- **SC-019**: Users can locate and fix every reported import error without trial
  and error, based on the file and row information in the report.

## Assumptions

- A single currency (EUR) is used for all amounts in version 1.
- A half-year is one of two periods: January 1 – June 30 and July 1 – December 31.
- A grave's term is counted in whole months, and a half-year can be invoiced only
  when at least one of its months lies inside the term.
- The "current half-year" is derived from the current date.
- A grave's net subtotals must divide evenly among its recipients; where they
  do not, one price per invoice block and VAT rate is raised by at most 1,00 €
  and the user confirms this on saving. Only a block that no line can fix within
  1,00 € is refused.
- For grave invoices generated before recipients were billed the grave's own
  lines directly, each line's stored price is one recipient's share; where the
  pre-split price is missing, it is reconstructed from the recipients' shares
  instead, which can be a cent or two off for very small amounts.
- Graves that existed before the field number, site name and position were
  introduced keep them empty until edited; the editor then requires them, and
  invoice generation does not.
- Graves that existed before the cemetery was introduced are put in one
  cemetery named "Unbekannt" when the database is upgraded, to be corrected in
  the editor; their site names, which may still name the cemetery, are left as
  they were. An empty database gets no cemetery.
- Generated documents are PDFs.
- Invoice numbers are sequential and unique; revisions are clearly marked as
  revisions.
- Version 1 assumes a single shared user; role-based permissions are out of scope.
- The user interface and the generated documents are German only.
- CSV files use comma separation with a header row and standard quoting/escaping;
  a file separated by semicolons, as Excel saves CSV in German, is read as well.
- The mapping CSV carries both link types — grave↔item (with count) and
  grave↔recipient — distinguished by a type column; the exact column syntax is
  finalized during planning.
- Version 1 import is create-only: identifiers that already exist in the
  application cause a validation error rather than an update.
- Duplicate identifiers within an archive are validation errors.
- Extra files inside the ZIP beyond the four CSVs are ignored; an empty CSV
  (header row only) is treated as "no data" for that file.
- The copy is a full duplicate of the invoice document, differing only by the
  semi-transparent diagonal stamp, which runs from one corner area of the page to
  the opposite one.
- The stamp word is a single globally configured text value.
- Normal invoices and copies are always downloaded via separate actions; no
  download mixes the two kinds.
- Error messages are shown in German.
