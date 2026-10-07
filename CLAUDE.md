# Working in this repository

The constitution in `docs/constitution.md` governs the project; deviations are
recorded in `docs/deviations.md`. Everything below is about running things.

## Never test against the dev stack

The E2E suite must run against its **own** stack:

```sh
cd e2e && npm test          # builds and starts docker-compose.e2e.yml on port 8081
```

`E2E_BASE_URL` points the suite at something already running instead; CI uses
it for the release image it has started. Do not point it at the dev stack on
port 8080: the specs create recipients, items, graves and invoices, and the API
has no DELETE endpoints for them, so whatever a run leaves behind stays in
`data/invoicing.db`. The dev database belongs to the user and holds their data.

The same goes for ad-hoc probing: seed and click through `docker-compose.e2e.yml`,
not through the dev stack. Seeding means writing the way the app writes: a grave
goes through `POST /odata/SaveGrave` with its items and recipients (`seedGrave`
in `e2e/seed.ts`), since the Graves, GraveItems and GraveRecipients sets take no
writes.

To start over on the dev database — an explicitly destructive step, so ask
first and commit before it: `scripts/reset-dev-db.sh`.

## A dialog takes focus when it finishes opening

A PrimeVue Dialog moves focus to its `autofocus` field once its opening
transition ends, and a Select's overlay hands focus back to its field on a
timeout after a pick. Playwright is faster than either, so a spec that starts
filling a dialog straight away, or moves on to the next field right after a
pick, is still typing when the focus change lands — and whatever focus-sensitive
thing it has opened in the meantime (a Select's overlay) closes underneath it.
CI is roughly four times slower than a dev machine and fails this where a local
run does not.

So the specs go through `e2e/vue.ts`: `waitForOverlayOpen` after opening a
dialog or drawer, `pickOption` (and `pickItem`, `pickRecipient`) for every
Select, `fillNumber` for an InputNumber, which only hands its value on when it
is left, and `saveEditor` for a drawer's save, which renders PDFs behind the
blocking overlay before the drawer closes. `pickOption` makes a pick again until
it has landed: the item and recipient pickers reload their lists whenever an
editor opens, and on a slow machine the answer replaces the option list while it
is open.

Layout bugs the specs have found are marked with `test.fail` and a `known bug:`
description, so the suite stays green while the bug is there and turns red once
it is fixed — remove the mark together with the fix.

## An overflowing page background paginates instead of repeating

The letterhead is drawn in `page.Background()`, which QuestPDF renders again on
every page — but only while its content fits. Ask for one millimetre more than
the page holds and it is treated as flowing content instead: page one gets the
top of it, and every later page gets nothing at all.

The symptom does not point at the cause. Nothing complains about heights; what
you see is continuation pages losing the whole letterhead, and the tests that go
red are the ones about images or footer text. `Letterhead.Compose` therefore
splits the page into three bands whose heights add up to exactly 297 mm, and
each band's content has to fit inside its own band. Two ways to break it, both
of which happened while it was being written: giving a band more content than it
reserves (moving the sender line down 4 mm was enough), and setting text in rows
shorter than the font's default line height — the footer's 3.4 mm rows need an
explicit `LineHeight` to fit 8.5 pt type.

Whatever you change there, check page two of a multi-page invoice, not just
page one.

## Running the parts

- **Dev stack**: `env "UID=$(id -u)" "GID=$(id -g)" docker compose up -d`
  (frontend on 8080, proxying `/odata` to the backend container).
- **Backend tests**: `scripts/dotnet.sh test Invoicing.sln`.
- **Frontend** (`frontend/`, Vue 3 and PrimeVue): `cd frontend && npm run
typecheck`, then `npm run lint` (ESLint, scoped to `src`) and `npm run build`.
  All three must be clean. For probing with hot reload, run it on the host
  against the E2E stack rather than the dev stack:
  `cd frontend && VITE_PROXY_TARGET=http://localhost:8081 npm run dev -- --port 5173`.
  After an EDM change, `npm run gen:odata` (reads `$metadata` from the E2E
  stack) and commit `src/api/metadata.xml` together with `src/api/generated/`.
- **E2E**: see above. `--project=desktop|phone|tablet` narrows it; the phone and
  tablet projects only run `tests/layout.spec.ts`. Tests run fully parallel,
  each one on its own: every test seeds its own data (names from `e2e/unique.ts`)
  and must not depend on another test or its order. The one exception is the
  serial group in `graves.spec.ts` around the current half-year, which a
  generation changes for every grave at once. CI shards the suite over three
  runners.
- **Release scripts**: `scripts/test-release-scripts.sh`.

## Formatting is automatic

Two formatters, split by directory. **CSharpier** owns `backend/`; **Prettier**
owns everything else — `.prettierignore` excludes `backend/` so no file answers
to both. Prettier also honours `.gitignore`, so `dist/`, `node_modules/` and
`data/` need no entry.

`.husky/pre-commit` runs `lint-staged`, which formats staged files in place. The
C# entry starts the SDK container, so it costs a couple of seconds — but only
when C# is actually staged; a docs-only commit never needs Docker. CI re-checks
both in the `format` job, since hooks only exist where `npm install` has been
run and `--no-verify` skips them.

By hand:

```sh
npm run format          # rewrite
npm run format:check    # fail if anything is unformatted
```

CSharpier goes through `scripts/csharpier.sh`, which runs it in the SDK
container like `scripts/dotnet.sh`. It parses syntax and never loads MSBuild, so
unlike `dotnet format` it does not restore into `backend/obj` and cannot collide
with the dev container — the conflict described at the end of this file does not
apply to it.

`printWidth` is 100 rather than Prettier's default 80, which is roughly where
this codebase already wrapped, and matches CSharpier's own default.

`git blame` skips the initial reformat via `.git-blame-ignore-revs`. To have it
do that automatically:

```sh
git config blame.ignoreRevsFile .git-blame-ignore-revs
```

## Commit messages are linted

Run `npm install` once at the repository root. It installs the `commit-msg`
hook that checks the message against Conventional Commits, and without it the
hook is simply absent — nothing warns you.

The format is not cosmetic: `scripts/next-version.sh` reads these messages to
decide whether the next release is a major, minor or patch, and
`scripts/changelog-entry.sh` turns them into the changelog. `feat` gives a
minor, `fix`/`perf`/`refactor` a patch, a `!` or a `BREAKING CHANGE:` footer a
major, and docs/test/chore/ci/style/build release nothing on their own.

Preview what a release would do, without releasing anything:

```sh
scripts/next-version.sh                       # e.g. 1.3.0
scripts/changelog-entry.sh "$(scripts/next-version.sh)"
```

And to release, instead of the "Run workflow" button in the GitHub UI:

```sh
scripts/release.sh          # or: npm run release
```

It previews the version, the changelog entry and the state of CI on the commit,
asks, dispatches `.github/workflows/release.yml` and follows the run. The
preview comes from a throwaway worktree at `origin/<branch>`, because that is
what the workflow releases — unpushed commits are not in it, and a branch with
nothing releasable on it stops the script instead of starting a run that
`next-version.sh` would fail a minute later.

## The dev container and the SDK container share `backend/obj`

`backend/` is bind-mounted into the dev backend container (`dotnet watch`) and
into the SDK container that `scripts/dotnet.sh` starts. They restore into
different NuGet folders and overwrite each other's `project.assets.json`, which
surfaces as `NETSDK1064: Package ... was not found`.

Before running backend tests: `docker compose stop backend`, delete the stale
output (`find backend -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +`),
run the tests, then `docker compose start backend`.
