# Vendored WoWDBDefs

Pinned copies of table definitions from [wowdev/WoWDBDefs](https://github.com/wowdev/WoWDBDefs), used by `Extractor.Dbc`'s `DbcClient` (via DBCD's `FilesystemDBDProvider`) instead of a live fetch — reproducible/offline builds, same reasoning as vendoring StormLib (see `.claude-docs/architecture.md`).

Pinned to commit `2b8d984019efdc0161a2ab6e3beafb3f3f0ae8c3` (2026-09-16).

To update a file: re-download from
`https://raw.githubusercontent.com/wowdev/WoWDBDefs/master/definitions/<Table>.dbd`
and note the new commit hash here.

| File | Purpose | Pinned commit |
|---|---|---|
| `Talent.dbd` | Talent.dbc — layout A (`0.7.0.3694-0.9.1.3810`, 20 fields) and layout B (`0.10.0.3892` onward, 21 fields) both present | `2b8d984` (2026-09-16) |
| `TalentTab.dbd` | TalentTab.dbc — 3 vanilla layouts, see gotchas.md | `2b8d984` (2026-09-16) |
| `Spell.dbd` | Spell.dbc — 16 separate vanilla-era layout blocks (huge table, changes constantly), but `Name_lang`/`NameSubtext_lang`/`Description_lang`/`SpellIconID` are present unchanged across all of them | `2b8d984` (2026-09-16) |
| `SpellIcon.dbd` | SpellIcon.dbc — trivial, unchanged for all of vanilla (`ID` + `TextureFilename`) | `267de35` (2020-09-04) |
