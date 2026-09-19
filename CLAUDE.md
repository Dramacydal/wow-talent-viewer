# CLAUDE.md — WoW Vanilla Talents Viewer

Сайт для просмотра и построения деревьев талантов оригинальной (vanilla) World of Warcraft для любого исторического патча/билда клиента. Данные извлекаются из самих клиентов (MPQ/DBC), а не хардкодятся.

Полный план проекта: `~/.claude/plans/zippy-tumbling-hummingbird.md` (фазы, обоснования решений, риски).

## Documentation index
- [.claude-docs/index.md](.claude-docs/index.md) — routing table; start here when unsure
- [.claude-docs/architecture.md](.claude-docs/architecture.md) — стек, структура репозитория, контракт схемы БД
- [.claude-docs/gotchas.md](.claude-docs/gotchas.md) — non-obvious footguns
- [.claude-docs/conventions.md](.claude-docs/conventions.md) — code style, naming, commits

## Стек
- **Экстрактор** (`extractor/`): C# / .NET 10 (LTS) — MPQ (StormLib P/Invoke) + DBC (DBCD + WoWDBDefs) + BLP→PNG (BLPSharp)
- **Backend** (`web/`): PHP 8.3 + Symfony 7.4 (LTS) + Doctrine + Twig
- **Frontend**: Vue 3 island в Twig-странице (дерево талантов), не полноценный SPA
- **БД**: MySQL — уже существующий удалённый сервер пользователя, никакого локального/Docker MySQL
- **Иконки**: `storage/icons/*.png`, имя файла = sha256 содержимого, дедуплицированы между талантами и билдами

## Commands
- Сборка экстрактора: `cd extractor && dotnet build`
- Прогон diagnostic/spike-команд: `dotnet run --project Extractor.Cli --no-build -- <command> ...` (список команд — в начале `Extractor.Cli/Program.cs`)
- Symfony: `cd web && composer install`, `php bin/console <command>` (PHP 8.3 через `update-alternatives`, реальный DB-коннект — в `web/.env.local`, не в git)
- Миграции схемы: `php bin/console doctrine:migrations:migrate` (новую миграцию — `doctrine:migrations:diff`, но **обязательно проверить сгенерированный файл на DROP/ALTER посторонних таблиц** перед применением — БД пользователя может быть общей с другими его инструментами)
- Экстракция всех билдов разом: `extractor/scripts/Extract-AllBuilds.ps1` (PowerShell, гоняется пользователем на его машине — ближе к БД, быстрее) — логи по каждому билду пишутся в `.tmp/extract-logs/<build>.log`, читать их напрямую вместо того, чтобы просить пользователя копировать консоль
- Дев-сервер сайта: `preview_start` с конфигом `symfony-web` из `.claude/launch.json` (`php -S` + `web/public/router.php` + `PHP_CLI_SERVER_WORKERS=4` — оба нюанса объяснены в gotchas.md, **сервер нужно перезапускать после любой правки `web/src/`**)

## Boundaries
### MUST
- Схема БД — Doctrine migrations в `web/` являются source of truth; экстрактор должен строго соответствовать этой схеме
- Все таблицы с данными о талантах скоуплены по `client_build_id` (дерево различается между патчами)
- Спецификации полей DBC брать из [wowdev/WoWDBDefs](https://github.com/wowdev/WoWDBDefs), не с wowdev.wiki
- Иконки — дедупликация по content-hash (см. `.claude-docs/architecture.md`), старые PNG никогда не удалять
- Перед `doctrine:migrations:migrate` на реальной БД — всегда читать сгенерированный файл миграции целиком; БД пользователя может быть общей с другими его инструментами (реальный случай: `doctrine:migrations:diff` сгенерировал `DROP TABLE builds` для чужой таблицы в общей БД — см. gotchas.md)
- `TalentTab.ID`/любые DBC ID — валидны только внутри одного билда, никогда не сопоставлять между билдами; осиротевшие ссылки (`TabID`, не резолвящийся в реальный `TalentTab` того же билда) — пропускать с логом, не писать битым FK (см. `.claude-docs/gotchas.md`)
- На проде — `APP_DEBUG=0` явно в `.env.prod.local`, не полагаться на неявный дефолт `symfony/runtime`. Необработанное исключение при включённом debug рендерит стектрейс с DSN из `DATABASE_URL` (пароль от БД) прямо в HTTP-ответе (см. `.claude-docs/gotchas.md`)

### MUST NOT
- Не поднимать Docker/локальный MySQL без явного запроса пользователя (см. `.claude-docs/architecture.md`)
- Не хардкодить тексты/номера/иконки талантов в коде — всё из БД, наполненной экстрактором
- **НИКОГДА не запускать `find /` (или любой `find`/`grep -r` от корня `/`) в этом окружении.** `/mnt/e` — это РЕАЛЬНЫЙ диск E: пользователя на его физической машине (не лупбэк/локальный контейнерный маунт) — рекурсивный обход от `/` проходит и через него, создавая реальную, заметную нагрузку на его компьютер, не только "медленно внутри контейнера". Уже дважды наступал на эти грабли в одной сессии (искал `QSpellWork`, потом `TalentFrame.xml`) — оба раза пользователь останавливал зависшую команду. Только `find <конкретный путь>` (`.`, `/tmp`, `/mnt/e/git/wow-talents-viewer/...` и т.п.), никогда голый `/`.

## Workflow
- Solo-разработка, без трекера задач. Default branch: `master`.
