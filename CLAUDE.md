# Working in this repository

The constitution in `docs/constitution.md` governs the project; deviations are
recorded in `docs/deviations.md`. Everything below is about running things.

## Never test against the dev stack

The E2E suite must run against its **own** stack:

```sh
cd e2e && npm test          # builds and starts docker-compose.e2e.yml on port 8081
```

`E2E_BASE_URL=http://localhost:8080` (`npm run test:dev`) points the suite at
the running dev stack instead. Do not use it: the specs create recipients,
items and invoices, and the API has no DELETE endpoints for them, so whatever a
run leaves behind stays in `data/invoicing.db`. The dev database belongs to the
user and holds their data.

The same goes for ad-hoc probing: seed and click through `docker-compose.e2e.yml`,
not through the dev stack.

To start over on the dev database — an explicitly destructive step, so ask
first and commit before it: `scripts/reset-dev-db.sh`.

## A dialog takes focus when it finishes opening

`sap.m.Dialog` moves focus to its first field once its opening animation
completes — about 230ms after `open()`. Playwright is faster than that, so a
spec that starts filling a dialog straight away is still typing when the focus
change lands, and anything focus-sensitive it has opened in the meantime (a
ComboBox suggestion popover) closes underneath it.

So a spec that opens a dialog calls `waitForDialogOpen(page)` from
`e2e/dialog.ts` before touching it. The helper explains the mechanism.

One deliberate exception: `invoices.spec.ts` does not use it. The wait moves
when that spec reads the recipient the dialog shows, and "the second invoice
goes to the recipient the reopened dialog shows" then fails about four runs in
five — including with the recipient handling reverted to what it was before the
model refactor, so the spec is sensitive to the timing rather than to the
control. It stayed green through the recipient field's change from a Select to
a ComboBox, so leave it as it is.

## Running the parts

- **Dev stack**: `env "UID=$(id -u)" "GID=$(id -g)" docker compose up -d`
  (frontend on 8080, proxying `/odata` to the backend container).
- **Backend tests**: `scripts/dotnet.sh test Invoicing.sln`.
- **Frontend checks**: `cd frontend && npm run typecheck`, then `npm run lint`
  (ESLint with type-aware rules) and `npm run ui5lint` (deprecated APIs,
  accessibility, manifest). All three must be clean; both linters are scoped to
  `src`, since `dist/` holds the whole OpenUI5 runtime.
- **E2E**: see above. `--project=desktop|phone|tablet` narrows it; the phone and
  tablet projects only run `tests/layout.spec.ts`.
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

## The dev container and the SDK container share `backend/obj`

`backend/` is bind-mounted into the dev backend container (`dotnet watch`) and
into the SDK container that `scripts/dotnet.sh` starts. They restore into
different NuGet folders and overwrite each other's `project.assets.json`, which
surfaces as `NETSDK1064: Package ... was not found`.

Before running backend tests: `docker compose stop backend`, delete the stale
output (`find backend -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +`),
run the tests, then `docker compose start backend`.
