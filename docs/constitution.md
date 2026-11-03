<!--
Sync Impact Report
==================
Version change: 6.0.0 → 6.0.1 (wording: the PrimeUI key is a repository secret)
Modified principles: Principle VI — the license key comes from the
  PRIMEUI_LICENSE repository secret rather than a repository variable
Modified sections: none
Added sections: none
Removed sections: none
Follow-up TODOs: none (the PRIMEUI_LICENSE secret exists)
-->

# rechnungen-burkard Constitution

## Core Principles

### I. Full-Stack Web Application

The application MUST be a web application composed of a frontend and a backend.
The frontend and backend MUST communicate through a clear, documented interface
using OData v4. The two MUST be released together as a single container image.

Rationale: A document-generation product requires both a user-facing interface
and a backend that owns business logic and persistence. One deployment target
keeps the release and the operations around it small; what keeps the two sides
separable is the OData interface, not a container boundary.

### II. Document Generation

The application MUST generate documents (e.g., invoices) as a core capability.
Document generation MUST be implemented as a deterministic, testable backend
service. Generated documents MUST be reproducible from persisted data.

Rationale: Document generation is the primary business purpose of the
application.

### III. Integration & E2E Testing Over Unit Testing

The project MUST NOT mandate fine-grained unit tests for every function. Test
effort MUST prioritize integration and end-to-end tests that exercise
user-visible behavior and system flows. Unit tests SHOULD be reserved for pure
business logic where they demonstrably reduce risk.

Rationale: Integration and E2E tests validate the system as a whole and provide
higher confidence per test than exhaustive unit coverage.

### IV. SQLite Persistence with Versioned Migrations

Data MUST be stored in SQLite. Every schema change MUST be accompanied by a
database migration. Each version MUST introduce at most one migration; multiple
schema changes within a version MUST be consolidated into a single migration.

Rationale: A single migration per version keeps schema evolution simple,
reviewable, and reproducible.

### V. Containerized Development & Release

Local development MUST run inside a Docker container. Releases MUST be
published as Docker container images on GitHub.

Rationale: Containers provide a consistent environment from local development
through production release.

### VI. Vue Frontend, Open Source with a PrimeUI Exception

The frontend MUST be built with Vue 3 and the PrimeVue component library. Every
library the frontend ships MUST be under an open-source license, with one
exception: the PrimeUI packages (`primevue`, `@primevue/*`, `@primeuix/*` and
`primeicons`) MAY ship under the commercial PrimeUI License. The licenses of
the bundled dependencies, the PrimeUI License included, MUST ship with the
frontend delivery.

The PrimeUI license key MUST NOT be committed. It is supplied at build time
(the `PRIMEUI_LICENSE` repository secret, passed to the image build), and
the frontend MUST keep working without it.

Rationale: Vue and PrimeVue provide the required UI framework. From PrimeVue 5,
PrimeUI publishes its packages under its own commercial license; the project
holds a PrimeUI license and stays on PrimeVue rather than migrating to another
component library. The exception is limited to PrimeUI so that no other
proprietary licensing obligations enter the frontend.

### VII. English Codebase, German UI

Source code, identifiers, comments, commit messages, and documentation MUST be
written in English. The application itself — its user interface and the
documents it generates — MUST be German, and German only. User-visible strings
in the frontend MUST NOT be hard-coded; they live in the i18n catalogue.

Rationale: English keeps the codebase maintainable for any contributor. The
application's users and the recipients of its invoices are German-speaking, so a
second language is upkeep without a reader. The catalogue keeps the texts in one
place, apart from the code that shows them.

### VIII. Blocking Progress Indicators for Long-Running Tasks

Whenever the application triggers a long-running backend task, the user
interface MUST show a progress indicator and MUST block interaction with the
affected interface until the task completes.

Rationale: Blocking progress feedback prevents duplicate submissions and
data inconsistency while giving users a clear signal that work is in progress.

## Technology Constraints

- Web application with a frontend and a backend.
- .NET as the backend implementation stack.
- OData v4 as the backend API protocol, consumed by the frontend through a
  client generated from `$metadata`.
- Vue 3 and PrimeVue for the frontend (PrimeUI License, see Principle VI).
- SQLite for data persistence.
- Docker for local development and release packaging.
- GitHub for source control, releases, and container image distribution.
- An i18n catalogue (vue-i18n) holding the German user-visible texts.

## Development & Release Workflow

- Local development MUST run inside a Docker container.
- Schema changes MUST include a database migration; exactly one migration per
  version.
- The application version MUST be incremented automatically on each release,
  following semantic versioning (MAJOR.MINOR.PATCH).
- Releases MUST be performed on GitHub by publishing the application as a
  Docker container image.

## Governance

This constitution supersedes all other development practices. Amendments MUST
be documented, versioned, and recorded with the amendment date. The
constitution version MUST follow semantic versioning: MAJOR for
backward-incompatible governance changes, MINOR for new principles or sections,
PATCH for clarifications and wording fixes.

Compliance with this constitution MUST be reviewed as part of code review and
release. Deviations MUST be justified and recorded.

**Version**: 6.0.1 | **Ratified**: 2026-08-15 | **Last Amended**: 2026-11-03
