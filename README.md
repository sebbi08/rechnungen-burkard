# rechnungen-burkard

Invoicing for Blumen Burkard: invoices for items and services, and grave care
billed per half-year to the people who share a grave. The interface and the
invoices are German. One container serves both the web interface and the API.

Working on the code: see [`CLAUDE.md`](CLAUDE.md) for how to run, test and
release, and [`docs/`](docs/) for the specification and the constitution that
governs the project.

## Running it

Each release is published as a container image:

```sh
docker run -d --name rechnungen \
  -p 8080:8080 \
  -v /srv/rechnungen:/data \
  --restart unless-stopped \
  ghcr.io/sebbi08/rechnungen-burkard:v3.0.0
```

Then open `http://<host>:8080`.

- **Port**: the app listens on 8080 inside the container.
- **Data**: everything — the database and every generated PDF — is one SQLite
  database at `/data/invoicing.db`. Mount `/data` from the host, or the data is
  gone with the container.
- **Time zone**: the image runs in `Europe/Berlin`, which decides the invoice
  date and the start of each year's invoice numbers. Leave it.
- **Access**: there is no login. Anyone who reaches the port can read and
  change everything, so expose it only on a network you trust, never to the
  internet.
- **Tags**: `vX.Y.Z` for each release, and `latest` for the newest one. Pin a
  version, so an upgrade happens when you choose it.

## Is it running, and which version?

```sh
curl http://<host>:8080/healthz
# {"status":"ok","version":"3.0.0"}
```

It answers 503 when the app cannot read its database. The image declares a
health check against it, so `docker ps` shows the container as `healthy` or
`unhealthy`.

## Backups

The database runs in SQLite's WAL mode: recent changes can still sit in
`invoicing.db-wal` next to the database file. A copy of `invoicing.db` alone,
taken while the app runs, can miss them or be inconsistent. So stop the
container, copy all of `/data`, and start it again:

```sh
docker stop rechnungen
cp -a /srv/rechnungen "/srv/rechnungen-backup-$(date +%F)"
docker start rechnungen
```

To restore, stop the container, put the copied files back into the data
folder, and start it.

### Backup from the app

To move the app to another server, or to take a backup by hand, use the
"Sicherung" card on the Import/Export page. "Sicherung herunterladen" gives one
ZIP file, `rechnungen-sicherung-YYYY-MM-DD-HHmm.zip`, with everything the app
keeps: graves, recipients, items, invoices with their PDFs, and the settings.
It is taken while the app runs. The file holds personal data, so store it
accordingly.

On the other instance, "Sicherung einspielen" takes that file, checks it and
shows what it holds. After you confirm, **all** data of that instance is
replaced by the backup's. A backup from an older version is migrated; one from
a newer version is refused, so update the target instance first. Downloading,
uploading and restoring are all refused while an invoice run or other job is
active.

The replaced database is not deleted. It stays next to the live one as
`invoicing.db.before-restore-<yyyyMMdd-HHmmss>`, with its `-wal` and `-shm`
files; the app never removes these, so delete them yourself when you no longer
need them. If a restore fails, the old database is put back by itself.

The scheduled file-copy backups above are not read by the app; restore them as
described there.

### Recovering from a failed restore

Should putting the old database back fail too, the app says so ("Die
Sicherung konnte nicht eingespielt werden, und die bisherigen Daten konnten
nicht automatisch zurückgelegt werden …"), answers every request with 503, and
leaves a marker file `invoicing.db.restore-incomplete` in the data folder. The
marker holds the path of the safety copy, and the log names it too. While the
marker is there the app refuses to start, so that it never runs on a half
swapped database. To repair it by hand:

1. Stop the container: `docker stop rechnungen`.
2. In the data folder, delete whatever is at the live path: `invoicing.db`,
   `invoicing.db-wal`, `invoicing.db-shm` and `invoicing.db-journal`, those of
   them that exist. Do not leave the `-wal` or `-shm` file behind: SQLite would
   apply it to the database you put back.
3. Move the safety copy named in the marker back under the live name, together
   with its `-wal` and `-shm` files if it has them, each to the matching live
   name. The marker gives the path inside the container (`/data/…`); on the
   host the file is in the data folder:

   ```sh
   cd /srv/rechnungen
   copy=invoicing.db.before-restore-20261112-143000   # the file name in the marker
   mv "$copy" invoicing.db
   [ -e "$copy-wal" ] && mv "$copy-wal" invoicing.db-wal
   [ -e "$copy-shm" ] && mv "$copy-shm" invoicing.db-shm
   ```

4. Delete the marker: `rm invoicing.db.restore-incomplete`.
5. Start the container: `docker start rechnungen`.

## Upgrading

Take a backup, then start the new version on the same data folder:

```sh
docker stop rechnungen && docker rm rechnungen
docker run -d --name rechnungen -p 8080:8080 -v /srv/rechnungen:/data \
  --restart unless-stopped ghcr.io/sebbi08/rechnungen-burkard:vX.Y.Z
```

The app brings the database up to date by itself when it starts. There is no
way back down: an older version cannot open a database a newer one has
migrated, so going back means restoring the backup taken before the upgrade.
What changed in each release is in [`CHANGELOG.md`](CHANGELOG.md).
