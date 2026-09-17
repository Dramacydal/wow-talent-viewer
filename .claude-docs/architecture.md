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
  - `extractor/native/win-x64/StormLib.dll` — precompiled StormLib v9.40 (MIT), официальный релиз **`stormlib_v9.40_amd64_RAD.zip`** (Release/Ansi/Dynamic) — сознательно не универсальный `stormlib_dll.zip`-бандл (у него неопределённая Ansi/Unicode сборка, не совпадает по хэшу ни с RAD, ни с RUD). Ansi-вариант выбран, чтобы совпадать по `char*`-семантике (`TCHAR`) с Linux-сборкой, — единый P/Invoke-код (`CharSet.Ansi`) работает на обеих платформах без разветвления.
  - `extractor/native/linux-x64/libStormLib.so` — тот же релиз, официальный Linux-пакет `libstorm-dev_v9.40_amd64.deb`, извлечён `.so` без системной установки.
  - Лицензия — `extractor/native/StormLib-LICENSE.txt`. При необходимости x86 — переизвлечь `stormlib_v9.40_x86_RAD.zip` из того же релиза.
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

## Диапазон поддерживаемых билдов (Talent.dbc)
Проверено прогоном `spike-dbc` по всем 43 клиентам пользователя (`/mnt/e/wow_data`):
- `0.5.3.3368`, `0.5.5.3494` — вне диапазона, талантов-грида ещё нет (см. gotchas.md — trainer-based механизм, нечего извлекать)
- `0.7.0.3694` – `0.9.1.3810` — `Talent.dbc` есть, **layout A**: 20 полей, recordSize=80
- `0.10.0.3892` – `1.12.2.6005` — `Talent.dbc` есть, **layout B**: 21 полей, recordSize=84 (стабильно до конца vanilla)

Т.е. за всю vanilla-историю структура `Talent.dbc` менялась всего один раз (между `0.9.1.3810` и `0.10.0.3892`) — на Фазу 1 достаточно двух вариантов маппинга полей, не десятка.

## UI-грид: константы из клиента (для фронтенда)
Обнаружено в `Blizzard_TalentUI.lua` (см. gotchas.md про сам сплит): классический vanilla-грид — `NUM_TALENT_COLUMNS = 4`, `MAX_NUM_TALENT_TIERS = 8`, `TALENT_BUTTON_SIZE = 32px`. Полезно как референс при вёрстке Vue-компонента дерева (Фаза 4) — геометрия зашита в клиенте, не только выводится из `tier`/`columnIndex` в `Talent.dbc`.

## Источники DBC-определений
Приоритет: `wowdev/WoWDBDefs` (source of truth, тот же что использует DBCD) → `suprsokr/VanillaDBDefs` (резерв только для релизных 1.x, без альфы) → собственный fallback WDBC-ридер для непокрытых альфа-билдов. Подробности и обоснование — в плане `~/.claude/plans/zippy-tumbling-hummingbird.md`.
