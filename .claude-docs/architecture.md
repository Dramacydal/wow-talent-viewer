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

### Схема БД (реализована)
Все таланто-таблицы скоуплены по `client_build_id`, т.к. дерево различается между патчами. Схема живёт как Doctrine migrations в `web/` (`web/src/Entity/*.php` — source of truth, `web/migrations/Version20260917053409.php` — применённая начальная миграция) — экстрактор обязан ей строго соответствовать.

БД пользователя — MariaDB 10.11.6 на удалённом сервере, отдельная выделенная база `wow_talent_viewer` (сознательно отделена от другой существующей БД пользователя `wow`, где уже была чужая таблица `builds` — см. gotchas.md). Подключение — `web/.env.local` (не в git).

`talent_tabs`/`talents` не переиспользуют DBC ID напрямую как PK (см. gotchas.md — DBC ID нестабильны/переиспользуются между билдами) — своя `id` (autoincrement) + `source_tab_id`/`source_talent_id` для трассировки и идемпотентного повторного запуска экстрактора. Осиротевшие `TabID`/`PrereqTalent`-ссылки (не резолвятся в реальную строку того же билда) при записи пропускаются — см. CLAUDE.md MUST.

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
- **Конвейер (реализован и провалидирован)**: `SpellIcon.TextureFilename` (без расширения) → `Extractor.Mpq.IconFileResolver` находит реальный файл в архиве (пробует `.blp`, потом `.tga` — расширение НЕ хардкодится; на всём проверенном диапазоне `0.5.5.3494`–`1.12.1.5875` реально только `.blp`, но код не полагается на это) → `Extractor.Blp.BlpConverter` декодирует через `wowdev/BLPSharp` (NuGet, MIT) и кодирует в PNG через `SixLabors.ImageSharp` **версии 3.1.12** (не 4.x!).
  **Лицензионный нюанс**: `SixLabors.ImageSharp` 4.x при сборке требует лицензионный ключ (новая монетизационная модель, build warning "No Six Labors license found"). Версия 3.1.12 (последний патч линии 3.x) — без этого требования, под старой Split License (бесплатно для проекта такого масштаба), и без известных уязвимостей (которые были в 3.1.6). Сознательно закреплена версия `3.1.12`, не апгрейдить на 4.x без пересмотра лицензии.
  Иконки живут в `interface.MPQ` (+патчи), НЕ в `dbc.MPQ` — нужен отдельный открытый архив.
- **Отложено (отдельная задача, не забыть)**: фоновые квадранты панели таба (`TalentTab.BackgroundFile` → `{name}-TopLeft/TopRight/BottomLeft/BottomRight.blp`, видели в `interface.MPQ`) пока НЕ извлекаются как картинки — в БД лежит только сырая строка-имя (`talent_tabs.background_file`). Извлечение через тот же `IconPipeline` — визуальный polish для Фазы 4 (фронтенд), не блокирует Фазу 1.
- **Дедупликация между билдами (реализована)**: `Extractor.Blp.IconPipeline` — сначала считает `source_hash` (sha256 сырых байт `.blp`/`.tga` из архива), проверяет `IIconSourceStore` по `(mpqPath, sourceHash)`; при совпадении декодирование полностью пропускается (`IconExtractionOutcome.CacheHit`), при несовпадении/отсутствии — декодирует и обновляет запись (`Decoded`). `IIconSourceStore` — интерфейс; текущая реализация `JsonFileIconSourceStore` — временная файловая заглушка вместо реальной MySQL-таблицы `icon_sources` (появится в Фазе 2), формат записи:
  ```json
  { "Interface\\Icons\\Spell_Fire_FlameBolt.blp": { "SourceHash": "bcce72d2...", "IconPath": "a89f00f6..." } }
  ```
  Проверено на практике: два прогона подряд на одной иконке дают `Decoded` → `CacheHit` с одинаковым `IconPath`, без повторного декодирования.

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

## DBC-чтение: DBCD (реализовано, не fallback-ридер)
`Extractor.Dbc.DbcClient` оборачивает [wowdev/DBCD](https://www.nuget.org/packages/DBCD) (NuGet, MIT, поддерживает `net10.0` напрямую):
- `MpqDbcProvider : IDBCProvider` — кормит DBCD байтами прямо из уже открытого/запатченного `MpqArchive`, без промежуточной экстракции на диск.
- `FilesystemDBDProvider` из самого DBCD — читает `.dbd` из `extractor/dbd-definitions/` (вендоренные, см. `dbd-definitions/README.md`, запиненный коммит WoWDBDefs), не тянет их из сети в рантайме.
- `dbcd.Load("Talent", build)` — `build` в формате `x.x.x.xxxxx`, **совпадает 1:1** с именами папок клиентов пользователя (`1.12.1.5875` и т.п.) — конвертация не нужна.
- Оба vanilla-layout'а (A/B, см. выше) подтверждены на реальных данных: `1.12.1.5875` (после фикса ID-бага, см. ниже) → talent ID=26 → tab ID=41 ("Fire", Mage) → spell 11069 = "Improved Fireball" Rank 1-5 с корректной прогрессией; `0.7.0.3694` → `RequiredSpellID` корректно отсутствует.
- Отсутствие колонки в конкретном layout'е (напр. `RequiredSpellID` в layout A) определяется через `storage.AvailableColumns.Contains(...)` **до** попытки чтения — попытка прочитать несуществующее для layout'а поле бросает исключение.
- **Собственный fallback WDBC-ридер под вопросом отменён**: `WoWDBDefs` содержит обе нужные структуры (layout A и B) явным текстом, DBCD успешно грузит обе — ручной ридер не понадобился.
- **КРИТИЧНЫЙ фикс**: `row.ID`/ключи `IDBCDStorage` для vanilla WDBC-таблиц не равны реальному полю ID (баг `DBCD.IO`, см. gotchas.md). `DbcClient` строит собственный индекс по `row.Field<int>("ID")` (`LoadIndexedById`) вместо `storage[id]`/`row.ID` — используется везде: и для `.Id` в наших record-моделях, и для лукапов (`GetSpell`, `GetSpellIcon`).
