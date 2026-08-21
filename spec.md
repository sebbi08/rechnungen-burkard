# Feature Specification: Invoicing App

**Feature Branch**: `001-invoice-generation`

**Created**: 2026-08-15

**Status**: Draft

**Input**: User description: "Combine spec 001 (invoice generation & grave management), 002 (data import & export), 003 (invoice copies with diagonal stamp) into one feature specification."

## Clarifications

### Session 2026-08-15

- Q: How is a VAT percentage assigned to an individual invoice line? → A: VAT rate is a fixed property of each invoice item; every invoice line always uses the item's rate.
- Q: When a grave has multiple recipients and the half-year amount is split evenly, what is generated? → A: One invoice per recipient, each containing only that recipient's share; the document states above the item list that it represents only XX% of the total invoice.
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

A user manages graves, each with a grave number, start and end date, a creation
date, one or more invoicing items (each with a count), and one or more recipients.

**Why this priority**: Graves are the core business object; their configuration is
needed before half-year invoices can be generated.

**Independent Test**: Can be fully tested by creating a grave with a number, dates,
one invoicing item, and two recipients, then listing and searching it.

**Acceptance Scenarios**:

1. **Given** the grave section is opened, **When** the user saves a grave with a
   number, start and end date, one item with count, and one or more recipients,
   **Then** the grave is stored with a creation date and is searchable.
2. **Given** the user searches for a grave, **When** they enter a grave number or
   recipient name, **Then** matching graves are returned.

---

### User Story 6 - Generate Grave Invoices per Half-Year (Priority: P2)

A user generates invoices for graves for a given half-year. Each recipient gets
their own invoice with their evenly split share, and the system records each
generation so the same grave cannot be invoiced twice for the same half-year.

**Why this priority**: Half-year grave invoicing with duplicate protection is the
central recurring business process.

**Independent Test**: Can be fully tested by generating invoices for a half-year,
then verifying that re-generating for the same half-year is blocked and no grave
appears twice.

**Acceptance Scenarios**:

1. **Given** graves with invoicing items and recipients exist, **When** the user
   generates invoices for a selected half-year, **Then** one invoice per recipient
   is created, each with the configured items, counts, and that recipient's share
   of the evenly split total.
2. **Given** a grave has multiple recipients, **When** its invoice is generated,
   **Then** each recipient receives their own invoice stating above the item list
   which percentage of the total it represents (e.g., 50% for two, 33.3% for three).
3. **Given** an invoice has already been generated for a grave and half-year,
   **When** the user attempts to generate again for the same half-year, **Then** the
   system prevents duplicates and shows that the grave is already invoiced.
4. **Given** the current half-year is active, **When** the grave overview is
   displayed, **Then** graves not yet invoiced for the current half-year are listed
   first.
5. **Given** the user selects a past or future half-year, **When** the view
   updates, **Then** the invoicing status for the selected half-year is displayed.

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
downloads never contain copies, and copies have their own bulk download.

**Why this priority**: Keeping the two document kinds separate in downloads
prevents accidentally sending copies where originals are expected.

**Independent Test**: Can be fully tested by bulk-downloading normal invoices and
verifying no copy is included, then bulk-downloading copies and verifying only
copies are included.

**Acceptance Scenarios**:

1. **Given** generated invoices with copies, **When** the user bulk-downloads
   normal invoices, **Then** only the normal documents are downloaded and no
   copies are included.
2. **Given** generated invoices with copies, **When** the user triggers the copy
   bulk download, **Then** only copies are downloaded.
3. **Given** an invoice with a copy, **When** the user downloads the invoice
   document, **Then** only the normal document is downloaded; the copy requires
   its own download action.

---

### User Story 9 - Browse, Filter, and Bulk Download (Priority: P3)

A user browses invoices by half-year, sees at a glance which graves were or were
not invoiced for a selected period, and bulk-downloads documents via the separate
normal and copy actions.

**Why this priority**: These conveniences build on the core flows and improve
day-to-day efficiency.

**Independent Test**: Can be fully tested by selecting different half-years,
verifying ordering and filtering, and downloading batches.

**Acceptance Scenarios**:

1. **Given** multiple invoices exist, **When** the user triggers the bulk download
   of normal invoices, **Then** all selected normal invoices are downloaded as
   documents, with no copies included.
2. **Given** the user browses an invoice, **When** they open its documents,
   **Then** the normal document and the copy are listed and distinguishable.

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
- How are rounding differences handled when a total is split evenly among three
  or more recipients? The remainder in the smallest currency unit must be
  distributed so the sum of shares always equals the total.
- What happens when a recipient's address is changed after invoices were
  generated? Historical invoices must remain unchanged.
- What happens when a grave's end date lies before the selected half-year?
  Historical generation for that half-year must remain possible and recorded once.
- What happens when a generated invoice is edited? The previous version is kept
  unchanged and the new document is saved as a revision with a clearly marked
  number.
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

## Requirements _(mandatory)_

### Functional Requirements

**Global configuration**

- **FR-001**: The system MUST allow configuring multiple VAT percentages globally.
- **FR-002**: The system MUST allow configuring a multi-line ending text globally.
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
- **FR-010**: Users MUST be able to select invoice items by their number when
  composing invoices.

**Invoice lines and totals**

- **FR-011**: Each invoice line MUST contain a count, a price per item, and a line
  total.
- **FR-012**: The system MUST calculate each line total as count × price per item.
- **FR-013**: The system MUST apply each invoice item's VAT rate to its line
  total.

**Recipients**

- **FR-014**: A recipient MUST have a name and an address.
- **FR-015**: Users MUST be able to manage recipients in a separate section.
- **FR-016**: Users MUST be able to add recipients while creating a grave or an
  invoice.
- **FR-017**: Changes to a recipient's name or address MUST only apply to future
  invoices; previously generated invoices MUST keep the values they were issued
  with.

**Graves**

- **FR-018**: A grave MUST have a grave number, a start date, and an end date.
- **FR-019**: A grave MUST support one or more invoicing items, each with a count.
- **FR-020**: A grave MUST support one or more recipients.
- **FR-021**: When a grave has multiple recipients, the system MUST generate one
  invoice per recipient, each containing only that recipient's share of the
  evenly split total.
- **FR-022**: Each recipient invoice MUST state, above the item list, which
  percentage of the total grave invoice it represents.
- **FR-023**: The system MUST record a creation date for each grave.
- **FR-024**: Users MUST be able to search graves.

**Half-year grave invoicing**

- **FR-025**: Grave invoices MUST be generated per half-year period.
- **FR-026**: The system MUST record that a grave's invoices have been generated
  for a given half-year.
- **FR-027**: The system MUST prevent generating duplicate invoices for the same
  grave and the same half-year.
- **FR-028**: The grave overview MUST list graves not yet invoiced for the current
  half-year first.
- **FR-029**: Users MUST be able to select a half-year to view graves invoiced in
  past or future half-years.

**Non-grave invoices**

- **FR-030**: Users MUST be able to generate invoices that are not tied to a grave.
- **FR-031**: A non-grave invoice MUST be bound to exactly one recipient.
- **FR-032**: A non-grave invoice MUST support one or more items, selected by
  number, each with a count and an optional price.

**Invoice revisions**

- **FR-033**: Users MUST be able to edit a generated invoice.
- **FR-034**: Editing an invoice MUST create a new revision; the previously
  generated version MUST be preserved unchanged.
- **FR-035**: The invoice number of a revision MUST clearly indicate that it is a
  new revision of an existing invoice.
- **FR-036**: Invoices MUST NOT be deletable.

**Documents and copies**

- **FR-037**: The system MUST generate, for every invoice (grave and non-grave),
  a normal invoice document and a copy document in the same generation step.
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

- **FR-045**: Users MUST be able to bulk-download generated invoices.
- **FR-046**: Bulk download of normal invoices MUST NOT include copies.
- **FR-047**: The system MUST provide a separate bulk download action for copies.
- **FR-048**: Downloading a single invoice MUST deliver only the normal document;
  the copy MUST be downloadable via its own action.

**Import and export**

- **FR-049**: The system MUST provide a downloadable ZIP archive containing
  exactly four CSV files with header rows: recipients, invoice items, graves, and
  a mapping file.
- **FR-050**: The recipients CSV MUST capture a recipient number, a name, and an
  address.
- **FR-051**: The items CSV MUST capture item number, text, VAT rate, and an
  optional price.
- **FR-052**: The graves CSV MUST capture the grave number and start and end
  dates.
- **FR-053**: The mapping CSV MUST link each grave to its invoicing items (item
  identifier with count) and to its recipients (recipient number), covering both
  link types.
- **FR-054**: The system MUST validate an archive before importing any data.
- **FR-055**: Validation MUST check that all four CSV files are present.
- **FR-056**: Validation MUST check that each CSV file has a correct header row.
- **FR-057**: Validation MUST check each row for missing or invalid required
  fields.
- **FR-058**: Validation MUST check cross-file consistency: every invoice item
  and recipient referenced in the mapping CSV MUST be defined in the items or
  recipients CSV file.
- **FR-059**: Validation MUST report each problem with the affected file, the row,
  and a human-readable reason.
- **FR-060**: The system MUST NOT import any data when validation reports one or
  more errors (all-or-nothing).
- **FR-061**: The system MUST create recipients, invoice items, and graves from a
  valid archive, linking each grave's items and recipients via the mapping CSV.
- **FR-062**: After a successful import, the system MUST show a summary of the
  imported data (counts per file).

**Progress and language**

- **FR-063**: During a long-running task (import or batch generation), the user
  interface MUST show a progress indicator and MUST block interaction with the
  affected interface until the task completes.
- **FR-064**: The application MUST support multiple languages for all
  user-visible text.

### Key Entities _(include if feature involves data)_

- **Recipient**: A person or organization; has a recipient number, a name, and an
  address. Each generated invoice captures the recipient values used at the time
  of generation.
- **Invoice Item**: A catalogue entry identified by a number, with a text, a VAT
  rate, and an optional default price.
- **Grave**: Identified by a grave number; has a start date, end date, creation
  date, one or more invoicing items (with counts), and one or more recipients.
- **Invoice**: A generated document; tied either to a grave (for a half-year, one
  invoice per recipient carrying that recipient's share and a percentage note) or
  to a single recipient (non-grave). Carries invoice lines, VAT, ending text,
  generation date, and a unique invoice number. Edits create new revisions;
  previous revisions are preserved and invoices cannot be deleted.
- **Invoice Line**: A single item on an invoice; has a count, a price per item,
  and a line total.
- **Invoice Document**: The generated PDF for an invoice revision; each revision
  has two documents — a normal document and a stamped copy.
- **VAT Rate**: A globally configured percentage, assigned to invoice items and
  applied to their line totals.
- **Global Settings**: Holds the VAT rates, the multi-line ending text, and the
  copy stamp word (default "Kopie").
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
- **SC-004**: For graves with multiple recipients, the sum of the recipient shares
  equals the invoice total, within the smallest currency unit.
- **SC-005**: A user can locate a specific grave by number or recipient name within
  30 seconds.
- **SC-006**: A bulk download of N normal invoices yields exactly N documents and
  never contains a copy.
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
- **SC-012**: A copy bulk download of N invoices yields exactly N copies.
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
- The "current half-year" is derived from the current date.
- When an amount is split evenly among recipients, any remainder in the smallest
  currency unit is assigned to the first recipient so shares always sum to the
  total.
- Generated documents are PDFs.
- Invoice numbers are sequential and unique; revisions are clearly marked as
  revisions.
- Version 1 assumes a single shared user; role-based permissions are out of scope.
- The user interface supports at least English and German, with additional
  languages addable later.
- CSV files use comma separation with a header row and standard quoting/escaping.
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
- The stamp word is a single globally configured text value, not translated per
  application language.
- Normal invoices and copies are always downloaded via separate actions; no
  download mixes the two kinds.
- Error messages are shown in the application's configured languages.
