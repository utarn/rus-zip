---
name: rus-zip-backup
description: >
  Back up and restore databases and storage trees with the rus-zip CLI using
  proven, verified sequences. Use when the user asks to back up a database
  (Postgres, MySQL, SQLite, or any engine with a dump tool), back up a
  directory or storage tree, take a full or incremental backup, create dated
  backup archives, split a large backup into volumes, encrypt an offsite
  backup copy, restore a database from a rus-zip archive, restore a directory
  backup, delete files from a backup chain, or verify a backup archive's
  integrity — including .zrus full archives, --baseline incrementals, and
  tombstone deletion layers.
license: MIT
metadata:
  author: utarn
  version: "1.0.0"
---

# rus-zip-backup

Backup and restore workflows for databases and storage trees, built on the
`rus-zip` CLI. This skill assumes the **`rus-zip` skill's basics** — CLI
preflight/install, format choice, license gating, and the flag tables for
`compress`/`extract`/`list`/`test`/`append`. Read that skill first if you have
not already; this document covers backup-specific sequences only and does not
restate its command reference.

## The two invariants (unconditional)

**Backup invariant — every backup, both domains:**

1. **Dump/collect** the data (engine dump tool for databases; nothing to
   collect for a plain storage tree).
2. **Compress** into the archive.
3. **Integrity-test** the archive (`rus-zip test <ARCHIVE>`).
4. **Record SHA-256** of the archive file (`sha256sum <ARCHIVE>`).
5. **Report the artifact location** — absolute path plus the recorded
   checksum — to the user or log.

Never skip the test or the checksum, and never report a backup as done with
only a path and no verified artifact.

**Restore invariant — every restore, both domains:**

1. **Verify the checksum** recorded at backup time against the archive you
   have.
2. **Integrity-test** it (`rus-zip test <ARCHIVE>`).
3. **Extract to a staging directory** — never onto live data.
4. **Import/copy into place** (engine import for databases; `cp -a`/`rsync`
   for storage).
5. **Confirm** the result (application-level check: row counts, a test
   startup, a diff of a spot file).
6. **Clean up** the staging directory.

Extraction **never targets live data directly**. If there is no staging space
(or no recorded checksum to verify), stop and ask the user instead of
improvising.

Licensing is as in the `rus-zip` skill: `compress`, `append`, and `delete`
need a license token in `RUSZIP_LICENSE`; `extract`, `list`, and `test` are
free. If a command fails with a license error, tell the user — never ask them
to paste secrets into the transcript.

All `rus-zip` paths in the examples are relative for readability; the CLI
resolves them against the current directory. Use absolute paths for
artifacts you will reference across sessions (backups, checksum files,
staging directories).

## Databases

**Generic pattern (any engine):** use the engine's own dump tool to produce a
plain dump file, then compress that dump with rus-zip. The engine's dump tool
is authoritative for consistency and format; rus-zip adds compression,
encryption, split volumes, and a uniform archive layer.

> **Note:** some engines' native dump formats are already compressed (e.g.
> `pg_dump -Fc` custom format). Compressing those again wastes time for
> little gain — rus-zip still adds value for **encryption** (`--password`),
> **split volumes** (`--split-size`), or a **uniform archive layer** when a
> site mixes engines and wants one backup artifact shape.

**Worked example — Postgres.** Backup:

```bash
# 1. Dump (plain SQL format, so the archive does the compression)
pg_dump --no-password -h localhost -U postgres -d appdb -f /tmp/appdb.sql
# 2. Compress — profile high; encryption and/or split volumes optional
rus-zip compress /tmp/appdb.sql -o backups/appdb-$(date +%F).zrus --profile high
#    offsite/encrypted variant:
#    rus-zip compress /tmp/appdb.sql -o backups/appdb-$(date +%F).zrus \
#        --profile high --password "$BACKUP_PWD" --split-size 1GB
# 3. Integrity-test
rus-zip test backups/appdb-$(date +%F).zrus
# 4. Record checksum
sha256sum backups/appdb-$(date +%F).zrus | tee backups/appdb-$(date +%F).zrus.sha256
# 5. Report path + checksum; remove the plain dump from /tmp
rm /tmp/appdb.sql
```

Restore:

```bash
# 1–2. Verify checksum, then integrity-test (sha256sum -c from the same
# directory the checksum was recorded in)
sha256sum -c backups/appdb-$(date +%F).zrus.sha256
rus-zip test backups/appdb-$(date +%F).zrus [-p "$BACKUP_PWD"]
# 3. Extract to a STAGING directory, never onto live data
rus-zip extract backups/appdb-$(date +%F).zrus -o /tmp/restore-staging [-p "$BACKUP_PWD"]
# 4. Import into place
psql --no-password -h localhost -U postgres -d appdb -f /tmp/restore-staging/appdb.sql
#    (a pg_dump directory/custom-format dump would use pg_restore instead)
# 5. Confirm (row counts, app smoke test), then:
rm -rf /tmp/restore-staging
```

Every other engine (MySQL `mysqldump`, SQLite `.backup`/`sqlite3 .dump`, …)
follows the same generic pattern with its own dump/restore tool. Do not
improvise engine-specific flags; consult the engine's documentation.

## Storage backup

**Full backup is the default.** Compress the directory tree into a
date-suffixed `.zrus`:

```bash
rus-zip compress /srv/data -o backups/data-$(date +%F).zrus
rus-zip test backups/data-$(date +%F).zrus
sha256sum backups/data-$(date +%F).zrus | tee backups/data-$(date +%F).zrus.sha256
```

**Incremental backups are opt-in.** Pass `--baseline <previous.zrus>` and the
new archive stores **only files that changed** (plus tombstones for files
deleted since the baseline). Without the flag you always get a full archive —
never add `--baseline` unless the user asked for incremental backups.

```bash
# Later runs: diff against the WHOLE chain, layers oldest-first (repeat the
# flag — space-separating several archives after one --baseline does not work;
# the extras are treated as additional sources):
rus-zip compress /srv/data -o backups/data-$(date +%F).zrus \
    --baseline backups/data-<FULL_DATE>.zrus \
    --baseline backups/data-<INC1_DATE>.zrus
```

- **Baseline the whole chain, not just the last layer.** An incremental stores
  only its diff, so diffing against the previous incremental alone sees files
  unchanged since earlier layers as new. List every layer of the chain
  oldest-first (full first), or diff against the full alone for shallow
  chains.
- A run where **nothing changed** writes no archive (the command exits
  successfully as a no-op). Pass `--allow-empty` when the user wants a
  no-change marker archive written into the chain.
- If the baselines are encrypted, pass the same `-p/--password` to `compress`
  and to the later `extract` — an encrypted chain uses one password
  throughout.
- **Deletion tracking:** files removed from the source since the baselines are
  stored in the incremental as tombstones, so they stay deleted on restore.
  To remove paths from the chain's state explicitly, `rus-zip delete <ARCHIVE>
  <PATHS...>` creates a tombstone-only deletion layer (by default named
  `<base>-incremental.zrus` beside the target, which it never modifies).
  `delete` works on record-bearing archives only — incrementals and chain
  roots; a plain full backup (created without `--baseline`) is refused. To
  make a deletable chain root, create the full backup with an empty baseline:
  `--baseline ""`. Tombstone layers slot into the chain like any other layer.

**Large/offsite copies:** split volumes and password protection work with
full and incremental backups alike:

```bash
rus-zip compress /srv/data -o backups/data-$(date +%F).zrus \
    --split-size 4GB --password "$BACKUP_PWD"
```

Volumes are named `.part1.zrus`, `.part2.zrus`, …; `test` and `extract` take
the first part (`<name>.part1.zrus`) and follow the volumes automatically.
**Retention cadence** (how many backups/days to keep, when to start a fresh
chain) is the user's policy — do not delete or rotate old backups unless
asked.

## Restoring a backup chain

A differential incremental is restored **layered on its baseline(s)**:
`extract` accepts `--baseline` (repeated per layer, oldest first) with the
earlier archive(s), which are layered in argument order (later wins) before
the differential is applied — this also reapplies tombstones, so files
deleted in the chain do not reappear:

```bash
# 1–2. Verify checksums and test each layer in the chain
sha256sum -c backups/data-<LATEST_DATE>.zrus.sha256
rus-zip test backups/data-<LATEST_DATE>.zrus
# 3. Extract the chain into a STAGING directory
rus-zip extract backups/data-<LATEST_DATE>.zrus \
    --baseline backups/data-<FULL_DATE>.zrus \
    --baseline backups/data-<INC1_DATE>.zrus \
    -o /tmp/restore-staging
# 4–6. Copy into place, confirm, clean up
# Entries land at the staging root relative to the archived directory's
# contents (compressing /srv/data stores data/... as paths without the
# data/ prefix), so the staging root maps onto /srv/data:
rsync -a /tmp/restore-staging/ /srv/data/ && rm -rf /tmp/restore-staging
```

## Restore safety

Untrusted or suspect archives get guardrails; extraction defaults are
destructive, so treat them deliberately:

| Concern | Flag |
| --- | --- |
| Name collisions at the destination | `-c, --conflict <overwrite\|skip\|abort>` (default `overwrite`) |
| Never touch anything already on disk | `--no-overwrite` (aborts naming the conflicting path) |
| Decompression-bomb size cap | `--max-uncompressed-size <SIZE>` (default 64GB; `0` = unlimited) |
| Decompression-bomb entry-count cap | `--max-entries <COUNT>` (default 1,000,000; `0` = unlimited) |

For an archive from an untrusted source, keep the caps at their defaults and
check `rus-zip list` output for suspicious sizes/entry counts before
extracting. Only raise a cap when the user's own archive genuinely exceeds
it — raising one disables a corruption-and-malware guard. Never extract an
untrusted archive onto live data even with the caps in place; staging is still
mandatory.
