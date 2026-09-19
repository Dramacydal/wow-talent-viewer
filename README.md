# WoW Vanilla Talents Viewer

A viewer and interactive planner for World of Warcraft Vanilla (patches 0.7.0–1.12.2) talent
trees, for any historical client build the user owns locally. Pick a class and a specific
client version, see the real talent tree for that exact patch, and build a spec by clicking
through it — same rules as the original in-game UI (tier unlocks at 5 points spent per tier,
prerequisite ranks, etc.).

All data — talent names, descriptions, icons, tiers, prerequisites, ability costs/ranges,
even background art — is **extracted directly from the game clients' own MPQ/DBC files**,
never hand-entered. The talent tree genuinely changed shape release to release throughout
vanilla's development, so each client build gets its own independently-extracted, scoped copy
of the data.

## How it works

```
[WoW client files]
   │  (dbc.MPQ, interface.MPQ, patch-*.MPQ, locales)
   ▼
[C# Extractor CLI]  — StormLib (MPQ) + DBCD (DBC) + BLPSharp (BLP→PNG) —
   │  writes rows scoped to one client_build_id
   ▼
[MySQL]  ← schema owned by Doctrine migrations in web/ (source of truth)
   ▲
   │  read via Doctrine repositories
[Symfony backend]  — JSON API + Twig pages —
   │  serves one build+class's whole talent tree in a single request
   ▼
[Vue 3 island]  — grid render, click-to-spend, prerequisite lines, permalink —
```

The extractor is a one-shot, idempotent CLI tool: point it at a client directory and a build
label, and it populates (or re-populates) that one build's rows in MySQL. The web app never
touches the game files directly — it only reads what the extractor already wrote.

## Stack

- **Extractor** (`extractor/`): C# / .NET 10 — MPQ archives via a StormLib P/Invoke wrapper,
  DBC tables via [DBCD](https://www.nuget.org/packages/DBCD) (schema definitions from
  [wowdev/WoWDBDefs](https://github.com/wowdev/WoWDBDefs), vendored under
  `extractor/dbd-definitions/`), BLP→PNG via BLPSharp.
- **Backend** (`web/`): PHP 8.3 + Symfony 7.4 + Doctrine ORM/migrations + Twig.
- **Frontend**: a single Vue 3 component mounted into one Twig page (not a full SPA) — the
  rest of the site is server-rendered.
- **Database**: MySQL/MariaDB. No Docker, no bundled local database — this project expects an
  existing MySQL server the user already has, configured via `web/.env.local`.
- **Assets**: extracted icons and background art live on disk under `storage/`, deduplicated
  by content hash, served through Symfony via symlinks into `web/public/`.

## Repository layout

```
extractor/              C# solution
  Extractor.Cli/         diagnostic + extraction commands (entry point)
  Extractor.Mpq/         StormLib P/Invoke wrapper (MPQ archive reading)
  Extractor.Dbc/         DBCD wrapper, DBC record models
  Extractor.Blp/         BLP→PNG decoding, icon dedup pipeline
  Extractor.Storage/     writes extracted data into MySQL
  dbd-definitions/        vendored WoWDBDefs table definitions (pinned commit)
  scripts/                Extract-AllBuilds.ps1 — runs extraction across every local client
web/                     Symfony application
  src/Entity/             Doctrine entities — schema source of truth
  src/Controller/         Twig pages + the JSON talent-tree API
  assets/talent-tree.js   the Vue island
  migrations/             Doctrine schema migrations
storage/
  icons/                  talent/ability icons, named by sha256 of the PNG content
  class-icons/            the 9 class icons (from the character-creation sprite sheet)
  backgrounds/            per-tab background art (4 quadrants per tab)
.claude-docs/            deeper project documentation (architecture, gotchas, conventions)
```

## Database schema

Every talent-related table is scoped by `client_build_id`, since the tree's shape differs
between patches:

- `client_builds` — one row per extracted client (label, build number)
- `classes` — the 9 playable classes (static, not build-scoped)
- `talent_tabs` — one row per class's tab in a given build (name, icon, background quadrants)
- `talents` — tier/column position within a tab, max rank
- `talent_ranks` — per-rank spell data: name, description, icon, and (for active abilities)
  resolved cost/range/cast-time/cooldown, plus any stance/weapon requirement text
- `talent_prerequisites` — which talent+rank another talent requires
- `icon_sources` — a global (non-build-scoped) cache mapping an MPQ file's content hash to its
  already-decoded PNG, so re-running extraction on an unchanged icon never re-decodes it

Doctrine entities under `web/src/Entity/` are the schema's source of truth; the extractor is
written to match them exactly, and any schema change goes through
`php bin/console doctrine:migrations:diff` (always reviewed by hand before applying — see
Notes below).

## Prerequisites

- .NET 10 SDK (for the extractor)
- PHP 8.3 with the usual Symfony extensions (`mysqli`/`pdo_mysql`, `mbstring`, `xml`, `curl`,
  `intl`, `zip`, `gd`, `opcache`), plus Composer
- An existing MySQL or MariaDB server (local or remote) — no version is bundled
- One or more WoW Vanilla client installations on disk (any build from `0.7.0.3694` through
  `1.12.2.6005` has a usable `Talent.dbc`; earlier alpha clients predate the talent-grid UI
  entirely and have nothing to extract)

## Getting started

1. **Configure the database.** Create an empty MySQL/MariaDB database, then point
   `web/.env.local` (git-ignored) at it:
   ```
   DATABASE_URL="mysql://user:password@host:3306/dbname?serverVersion=<your-server-version>&charset=utf8mb4"
   ```
2. **Install PHP dependencies and apply the schema:**
   ```bash
   cd web
   composer install
   php bin/console doctrine:migrations:migrate
   ```
3. **Build the extractor:**
   ```bash
   cd extractor
   dotnet build
   ```
4. **Extract a client build** into the database:
   ```bash
   dotnet run --project Extractor.Cli --no-build -- extract-build \
     "<path-to-client-directory>" "<build-label, e.g. 1.12.1.5875>" \
     "<path-to-web/.env.local>" "<path-to-storage/icons>"
   ```
   To extract every client under one directory at once, use
   `extractor/scripts/Extract-AllBuilds.ps1` (PowerShell) instead — it skips the handful of
   pre-talent-grid alpha builds automatically and logs each build's output separately under
   `.tmp/extract-logs/`.
5. **Run the site:**
   ```bash
   cd web
   php -S localhost:8000 -t public public/router.php
   ```
   Open `http://localhost:8000`, pick a build and a class.

## Notes

- The database schema is owned by Doctrine migrations, not hand-edited SQL. Before applying a
  generated migration to a real database, always read the generated file in full — if that
  database is shared with anything else, a naive `doctrine:migrations:diff` can propose
  dropping tables that belong to something else entirely.
- Nothing here is hardcoded from external wikis or memory: talent text, icons, requirements
  and background art are all resolved from the client's own DBC/MPQ data at extraction time.
  Where a display convention needed reverse-engineering (e.g. which `Spell.dbc` bit means
  "requires this shapeshift form"), it was verified against real client data before being
  relied on — see `.claude-docs/gotchas.md` for the full history of what was checked and why.
