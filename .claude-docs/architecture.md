---
tags: [memory/repo, architecture]
---

# Architecture

## Поток данных

```
[WoW client files]
   │  (.mpq: patch.mpq, dbc.mpq, common.mpq, локали)
   ▼
[C# Extractor CLI]  -- StormLib (MPQ) + DBCD (DBC) + BLPSharp (BLP→PNG) --
   │  пишет строки, привязанные к client_build_id
   ▼
[MySQL]  <-- общая схема (Doctrine migrations в web/ — source of truth)
   ▲
   │  читает (Doctrine repositories)
[Symfony backend]  -- REST/JSON эндпоинты + Twig-страницы --
   │  отдаёт JSON дерева талантов на страницу
   ▼
[Vue 3 island]  -- рендер дерева, клики, live-валидация, permalink билда --
```

## Структура репозитория
- `extractor/` — C# solution (.NET 10): `Extractor.Cli`, `Extractor.Mpq` (P/Invoke над StormLib), `Extractor.Dbc` (DBCD + fallback WDBC-ридер), `Extractor.Blp` (BLPSharp)
  - `extractor/native/win-x64/StormLib.dll` — precompiled StormLib v9.40 (MIT), скачан с официального [GitHub Release](https://github.com/ladislav-zezula/StormLib/releases/tag/v9.40), не собирался из исходников. Лицензия — `extractor/native/StormLib-LICENSE.txt`. Только x64/Release/Unicode/Dynamic вариант; при необходимости x86 — переизвлечь из того же `stormlib_dll.zip` релиза.
- `web/` — Symfony 7.4 (PHP 8.3): backend, Doctrine migrations (source of truth схемы БД), Twig, Vue-island
- `storage/icons/` — PNG-иконки талантов на диске, путь общий для экстрактора (пишет) и Symfony (раздаёт статику)

## БД
MySQL — существующий удалённый сервер пользователя. Никакого локального MySQL/Docker (сознательное решение, см. gotchas.md).

### Схема БД
Все таланто-таблицы скоуплены по `client_build_id`, т.к. дерево различается между патчами. Схема живёт как Doctrine migrations в `web/` — экстрактор обязан ей строго соответствовать.

- `client_builds` (id, label, build_number, notes)
- `classes` (id, slug, name) — статичный справочник, не зависит от билда
- `talent_tabs` (id, client_build_id, class_id, name, icon, order_index, background)
- `talents` (id, client_build_id, talent_tab_id, tier, column_index, max_rank)
- `talent_ranks` (id, talent_id, rank_index, spell_id, name, description, icon_path)
- `talent_prerequisites` (talent_id, requires_talent_id, requires_rank)
- `icon_sources` (mpq_path, source_hash, icon_path) — **глобальная**, без `client_build_id`, кэш идентичности иконок между прогонами экстрактора (см. gotchas.md)

## Иконки
- PNG в `storage/icons/<sha256(PNG-содержимого)>.png`, дедуплицированы по контенту.
- Инкрементальность между билдами через `icon_sources`: см. [gotchas.md](gotchas.md).
- Старые PNG никогда не удаляются — на них ссылаются `talent_ranks` более старых билдов.

## Источники DBC-определений
Приоритет: `wowdev/WoWDBDefs` (source of truth, тот же что использует DBCD) → `suprsokr/VanillaDBDefs` (резерв только для релизных 1.x, без альфы) → собственный fallback WDBC-ридер для непокрытых альфа-билдов. Подробности и обоснование — в плане `~/.claude/plans/zippy-tumbling-hummingbird.md`.
