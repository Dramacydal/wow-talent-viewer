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
- `storage/icons/`, `storage/class-icons/`, `storage/backgrounds/` — PNG-картинки на диске, три СОСЕДНИЕ, но раздельные папки по типу ассета (см. ниже) — не смешивать разные типы в одну папку. Каждая раздаётся Symfony через свой симлинк (`web/public/icons`, `web/public/class-icons`, `web/public/backgrounds`)

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
- **Фоновые квадранты панели таба (реализовано)**: `TalentTab.BackgroundFile` → 4 картинки `Interface\TalentFrame\{BackgroundFile}-{TopLeft,TopRight,BottomLeft,BottomRight}.blp` из `interface.MPQ`, извлекаются per-build (не глобально, как class-icons — фон может отличаться между патчами так же, как и само дерево, см. `BuildExtractor.ResolveBackgroundQuadrant`). Держим 4 отдельные картинки, НЕ склеиваем в одну композитную — так же, как их использует реальный клиент. Комбинированный канвас всех 4 квадрантов — 320×384, но правые ~64px и нижние ~54px в исходной текстуре полностью ПРОЗРАЧНЫ (alpha=0, не чёрные — проверять альфа-канал, не RGB; см. gotchas.md), реальная непрозрачная область — фиксированные 300×330 у всех классов/табов. На фронте каждый квадрант кадрируется до своей доли этой непрозрачной области и растягивается на всю ширину/высоту грида (`.tt-tab-art-corner.{tl,tr,bl,br}` — 4 отдельных `position:absolute` дива в `assets/talent-tree.js`/`talent-tree.css`, не единый multi-layer background) под тёмной виньеткой (`::before`).
  Пишутся отдельным `IconPipeline`-инстансом (`backgroundPipeline` в `BuildExtractor`) в **свою** папку `storage/backgrounds/` — не в `storage/icons/` — при этом делят ОДНУ и ту же глобальную `icon_sources`-таблицу дедупликации (ключ — MPQ-путь, коллизий с обычными иконками способностей быть не может). Колонки `talent_tabs.background_{top_left,top_right,bottom_left,bottom_right}_path` — только имя файла (sha256), URL строит `TalentTreeController::backgroundUrl()` с префиксом `/backgrounds/` (отдельная от `iconUrl()`/`/icons/` функция — см. gotchas.md про баг, когда их перепутали).
- **Иконки классов (реализовано)**: `classes.icon_path` — те же 9 картинок, что использует экран создания персонажа, вырезаны ИЗ ОДНОГО спрайт-листа `Interface\Glues\CharacterCreate\UI-CharacterCreate-Classes.blp` (256×256, сетка 4×3, Paladin один в третьем ряду). Это **не build-scoped** экстракция — `classes` глобальная таблица (класс не меняется между патчами), команда `extract-class-icons <client> <envFile> <storageIconsDir>` в `Extractor.Cli` запускается один раз против любого билда, где есть все 9 классов (использован `1.12.1.5875`), а не гоняется по каждому клиенту. Координаты вырезки (`CLASS_ICON_TCOORDS`) взяты буквально из `Interface\GlueXML\CharacterCreate.lua` самого клиента — не подобраны на глаз (те же несколько px обрезки по краям от бесшовности, что и в оригинале: `0.49609375` вместо `0.5` и т.п.). Результат — та же content-hash-дедуплицированная `storage/icons/`, что и у иконок способностей.
  Этот же приём (найти `SetTexCoord`/UV-таблицу в `.lua` реального клиента, а не гадать по картинке) — рабочий шаблон и для отложенной задачи выше про фоновые квадранты таба, если та когда-нибудь понадобится.
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

## Symfony backend (Фаза 3, реализовано)
- Стек: `symfony/twig-bundle` + `symfony/asset-mapper` (никакого Node/npm/webpack — чистый PHP-стек, ESM через importmap, вендоренные копии npm-пакетов через `bin/console importmap:require`).
- Vue 3 подключен через importmap как **runtime-only** сборка (`vue.runtime.esm-bundler.js` — то, что реально скачивает `importmap:require vue`, без компилятора шаблонов). Значит **никаких строковых `template: "..."` в компонентах** — только `h()`-рендер-функции (см. `assets/talent-tree.js`).
- Роуты (все — атрибутами `#[Route]` в контроллерах, `config/routes.yaml` просто их подхватывает):
  - `GET /` — `HomeController::index` — Twig-пикер (server-rendered `<select>` из репозиториев), редирект на `/tree/{label}/{slug}` через vanilla JS
  - `GET /tree/{buildLabel}/{classSlug}` — `HomeController::tree` — Twig-страница с контейнером `<div id="talent-tree-app" data-tree-url="...">`, монтируется Vue
  - `GET /api/builds`, `GET /api/classes` — простые справочники (ручной `$this->json(...)`, без Serializer-компонента — формы данных маленькие и стабильные, не оправдывают доп. зависимость)
  - `GET /api/builds/{buildLabel}/classes/{classSlug}/talent-tree` — главный эндпоинт (`Api\TalentTreeController`), отдаёт ВСЁ дерево одним запросом (табы+таланты+ранги+пререквизиты), без follow-up запросов с фронта
- Иконки раздаются через симлинк `web/public/icons -> ../../storage/icons` (создан вручную, не автоматизирован — при переносе на новый сервер пересоздать).
- JSON API возвращает `iconUrl` уже готовым (`/icons/{hash}.png`), а не голый хэш — фронту не нужно знать паттерн пути.
- Repository-методы (`findForBuildAndClass` и т.п.) написаны с `JOIN FETCH` (`addSelect('rank')` и т.д.) специально чтобы не бить N+1 запросами по 300+ талантам на удалённую БД — критично при реальной сетевой задержке ~300мс с этого хоста (см. gotchas.md про латентность).
- **Дев-сервер**: `.claude/launch.json` гоняет `php -S` с `router.php` (см. `web/public/router.php` и его докблок) и `PHP_CLI_SERVER_WORKERS=4` — оба нюанса задокументированы в gotchas.md как реальные пойманные баги, не теоретические. **После правки любого файла в `web/src/` сервер нужно перезапускать** (воркеры держат классы в памяти).

## Vue-остров: интерактивное дерево (Фаза 4, реализовано)
- `web/assets/talent-tree.js` — единственный компонент, монтируется на `#talent-tree-app` (см. `templates/tree/show.html.twig`), тянет `/api/builds/{label}/classes/{slug}/talent-tree` один раз при монтировании.
- **Vue переключён на полную ESM-сборку с компилятором**, закоммичена вручную как `web/assets/lib/vue.esm-browser.js` (скачана с jsDelivr, версия `3.5.43`; `importmap.php`'s `vue` указывает на неё через `'path'`, а не `'version'`) — стандартная `importmap:require vue` тянет **runtime-only** сборку без компилятора шаблонов, а строковый `template: "..."` компоненту жизненно нужен (h()-рендер-функции для сетки такого размера были бы нечитаемы). **Важно**: файл лежит в `assets/lib/`, а НЕ в `assets/vendor/` (тот в `.gitignore`, пересоздаётся `composer install`/`importmap:install` обратно в сломанную runtime-only версию) — см. gotchas.md.
- **Правила клика** (обе проверяются на каждый клик, не только визуально):
  - потратить очко в таланте можно только если `totalSpentInTab(tab) >= talent.tier * 5` И все `prerequisites[].requiresTalentId` уже имеют `spent >= requiresRank`
  - убрать очко (правый клик/`@contextmenu.prevent`) блокируется, если это сделает НЕВАЛИДНЫМ любой уже потраченный талант в этом же табе (той же функцией `wouldStayValid`, которая проверяет и разлочку тира, и пререквизиты) — простая стратегия "симулировать → проверить весь таб → откатить, если что-то сломалось", не точечный per-dependent граф
- **Permalink** — `?build=<id>:<rank>,<id>:<rank>,...` в query string, пишется через `history.replaceState` при любом изменении `spent` (`$watch(..., {deep:true})`), читается при монтировании. Без БД — билд/класс уже в самом пути (`/tree/{label}/{slug}`), а распределение очков в query string, как и планировалось изначально.
- **Линии пререквизитов** — свой SVG-оверлей (`<line>` между центрами ячеек), координаты считаются в JS из тех же констант `CELL=44`/`GAP=6`, что и CSS grid (`talent-tree.css`) — если поменять один, обязательно поменять и другой, иначе линии разъедутся с сеткой.
- **Иконки фона таба** — реальные квадранты из клиента (см. раздел "Иконки" выше), НЕ образы, хотлинкнутые/скопированные с вовхеда. `.tt-tab-art` содержит 4 дива-угла (`cornerStyle(url)` в JS задаёт только `backgroundImage`, сам кроп/растяжение — фиксированные CSS-проценты в `talent-tree.css`, см. gotchas.md за математикой 300×330) плюс `.tt-grid`; поверх них — CSS `::before` с радиальным градиентом + `inset box-shadow` (тёмная виньетка, чтобы иконки талантов не терялись на фоне яркого арта). `.tt-tab-art` держит ЯВНУЮ пиксельную высоту (`tabArtStyle(tab)`, не auto) — обязательно для процентных `height` угловых дивов, см. gotchas.md.

## DBC-чтение: DBCD (реализовано, не fallback-ридер)
`Extractor.Dbc.DbcClient` оборачивает [wowdev/DBCD](https://www.nuget.org/packages/DBCD) (NuGet, MIT, поддерживает `net10.0` напрямую):
- `MpqDbcProvider : IDBCProvider` — кормит DBCD байтами прямо из уже открытого/запатченного `MpqArchive`, без промежуточной экстракции на диск.
- `FilesystemDBDProvider` из самого DBCD — читает `.dbd` из `extractor/dbd-definitions/` (вендоренные, см. `dbd-definitions/README.md`, запиненный коммит WoWDBDefs), не тянет их из сети в рантайме.
- `dbcd.Load("Talent", build)` — `build` в формате `x.x.x.xxxxx`, **совпадает 1:1** с именами папок клиентов пользователя (`1.12.1.5875` и т.п.) — конвертация не нужна.
- Оба vanilla-layout'а (A/B, см. выше) подтверждены на реальных данных: `1.12.1.5875` (после фикса ID-бага, см. ниже) → talent ID=26 → tab ID=41 ("Fire", Mage) → spell 11069 = "Improved Fireball" Rank 1-5 с корректной прогрессией; `0.7.0.3694` → `RequiredSpellID` корректно отсутствует.
- Отсутствие колонки в конкретном layout'е (напр. `RequiredSpellID` в layout A) определяется через `storage.AvailableColumns.Contains(...)` **до** попытки чтения — попытка прочитать несуществующее для layout'а поле бросает исключение.
- **Собственный fallback WDBC-ридер под вопросом отменён**: `WoWDBDefs` содержит обе нужные структуры (layout A и B) явным текстом, DBCD успешно грузит обе — ручной ридер не понадобился.
- **КРИТИЧНЫЙ фикс**: `row.ID`/ключи `IDBCDStorage` для vanilla WDBC-таблиц не равны реальному полю ID (баг `DBCD.IO`, см. gotchas.md). `DbcClient` строит собственный индекс по `row.Field<int>("ID")` (`LoadIndexedById`) вместо `storage[id]`/`row.ID` — используется везде: и для `.Id` в наших record-моделях, и для лукапов (`GetSpell`, `GetSpellIcon`).
