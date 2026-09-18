# Vendored WoWDBDefs

Pinned copies of table definitions from [wowdev/WoWDBDefs](https://github.com/wowdev/WoWDBDefs), used by `Extractor.Dbc`'s `DbcClient` (via DBCD's `FilesystemDBDProvider`) instead of a live fetch — reproducible/offline builds, same reasoning as vendoring StormLib (see `.claude-docs/architecture.md`).

Pinned to commit `2b8d984019efdc0161a2ab6e3beafb3f3f0ae8c3` (2026-09-16), with two local patches on top (upstream WoWDBDefs has real build-range gaps for two vanilla builds — see gotchas.md): `ChrClasses.dbd` and `Spell.dbd` each gained a `BUILD 1.0.1.3989` line, and `ChrClasses.dbd`'s `1.1.1.4062-1.9.4.5086` range was widened down to `1.1.0.4044-1.9.4.5086`.

To update a file: re-download from
`https://raw.githubusercontent.com/wowdev/WoWDBDefs/master/definitions/<Table>.dbd`
and note the new commit hash here.

| File | Purpose | Pinned commit |
|---|---|---|
| `Talent.dbd` | Talent.dbc — layout A (`0.7.0.3694-0.9.1.3810`, 20 fields) and layout B (`0.10.0.3892` onward, 21 fields) both present | `2b8d984` (2026-09-16) |
| `TalentTab.dbd` | TalentTab.dbc — 3 vanilla layouts, see gotchas.md | `2b8d984` (2026-09-16) |
| `Spell.dbd` | Spell.dbc — 16 separate vanilla-era layout blocks (huge table, changes constantly), but `Name_lang`/`NameSubtext_lang`/`Description_lang`/`SpellIconID` are present unchanged across all of them | `2b8d984` (2026-09-16) |
| `SpellIcon.dbd` | SpellIcon.dbc — trivial, unchanged for all of vanilla (`ID` + `TextureFilename`) | `267de35` (2020-09-04) |
| `ChrClasses.dbd` | ChrClasses.dbc — class names/`PlayerClass` (number used to build classMask = `1<<(PlayerClass-1)`); 4 vanilla layouts, only `Filename` missing before `0.5.5.3494` | `2b8d984` (2026-09-16) |
| `SpellDuration.dbd` | SpellDuration.dbc — one unchanged layout for all of vanilla; `Duration` (ms) needed for `$d`/`$o` escape sequences in Spell.Description | `2b8d984` (2026-09-16) |
| `SpellRadius.dbd` | SpellRadius.dbc — one unchanged layout for all of vanilla; `Radius` (yards) needed for `$a` escape sequences | `2b8d984` (2026-09-16) |
| `SpellRange.dbd` | SpellRange.dbc — one unchanged layout for all of vanilla; `RangeMin`/`RangeMax`/`Flags` needed for the "active ability" tooltip's range line (`Flags & 0x1` = melee range, verified on real data — id=2 is the only melee row, max=5yd) | `2b8d984` (2026-09-16) |
| `SpellCastTimes.dbd` | SpellCastTimes.dbc — one unchanged layout for all of vanilla; `Base` (ms) needed for the "active ability" tooltip's cast-time line | `2b8d984` (2026-09-16) |
| `SpellShapeshiftForm.dbd` | SpellShapeshiftForm.dbc — one row per real shapeshift form/stance (Cat Form, Battle Stance, ...); `Name_lang` resolves `Spell.ShapeshiftMask` bits into the "Requires X" tooltip line, see gotchas.md | `2b8d984` (2026-09-18) |
