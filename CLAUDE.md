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

## Running the parts

- **Dev stack**: `env "UID=$(id -u)" "GID=$(id -g)" docker compose up -d`
  (frontend on 8080, proxying `/odata` to the backend container).
- **Backend tests**: `scripts/dotnet.sh test Invoicing.sln`.
- **Frontend types**: `cd frontend && npm run typecheck`.
- **E2E**: see above. `--project=desktop|phone|tablet` narrows it; the phone and
  tablet projects only run `tests/layout.spec.ts`.

## The dev container and the SDK container share `backend/obj`

`backend/` is bind-mounted into the dev backend container (`dotnet watch`) and
into the SDK container that `scripts/dotnet.sh` starts. They restore into
different NuGet folders and overwrite each other's `project.assets.json`, which
surfaces as `NETSDK1064: Package ... was not found`.

Before running backend tests: `docker compose stop backend`, delete the stale
output (`find backend -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +`),
run the tests, then `docker compose start backend`.
