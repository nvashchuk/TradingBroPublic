# AGENTS.md — инструкции для AI-агентов

Контекст для AI-агентов, работающих с этим репозиторием. Если ты
человек — открывай `README.md` и `.claude/workflows/README.md`.

## Команды

```
dotnet build TradingBro.slnx                 # сборка всего solution
dotnet test  TradingBro.slnx                 # все тесты
dotnet test  TradingBro.slnx --filter "FullyQualifiedName~Bybit"
                                             # тесты одного коллектора
dotnet run --project src/TradingBro.Worker   # локальный запуск воркера
dotnet format                                # форматирование по .editorconfig
```

После каждой правки `.cs` файла сразу запускай `dotnet build` —
ловит большинство галлюцинаций (имена enum, namespace, сигнатуры).

## Структура проекта

```
src/TradingBro.Core           — абстракции и DTO, без зависимостей на другие проекты
src/TradingBro.Collectors     — сборщики анонсов по биржам (Binance, Bybit, Upbit, Bithumb)
src/TradingBro.Data           — EF Core / SQLite, миграции, DbContext
src/TradingBro.Strategies     — движок стратегий, paper trader, SpotListingShort
src/TradingBro.Trading        — live exchange-клиенты (Binance live + demo)
src/TradingBro.Notifications  — Telegram-бот
src/TradingBro.Worker         — host (точка входа)
tests/                        — xUnit + FluentAssertions
```

При добавлении новой биржи следуй паттерну
`src/TradingBro.Collectors/{Binance,Bybit,Upbit,Bithumb}/` — 4 файла:
`*Collector.cs`, `*Parser.cs`, `*Classifier.cs`, `*ApiModels.cs`.

## Стиль кода

- Цель: **.NET 10**, `<LangVersion>latest</LangVersion>`, `Nullable enable`.
- File-scoped namespaces (`namespace TradingBro.X;`), один публичный тип на файл.
- `PascalCase` для типов и публичных членов, `camelCase` для локальных,
  `_camelCase` для приватных полей.
- DTO для JSON — `System.Text.Json` + атрибуты `[JsonPropertyName(...)]`.
  Никакого Newtonsoft.
- Все async-методы принимают `CancellationToken` последним параметром.
- Логирование — Serilog через `ILogger<T>`, **только структурированное**:
  `logger.LogInformation("Polled {Count} from {Exchange}", n, exchange)`.
  Никаких `$"..."` внутри template-строки.
- Regex — через source-generator: `[GeneratedRegex(...)] private static partial Regex Foo();`.
- Записи (records) для immutable DTO, классы — для EF-сущностей.

## Тесты

- Фреймворк: **xUnit**, ассерты через **FluentAssertions**.
- Именование: `MethodName_Scenario_ExpectedResult`. Пример:
  `Classify_LaunchpoolTitle_ReturnsLaunchpoolAnnounce`.
- Фикстуры — **реальные** JSON-ответы биржевых API под
  `tests/.../Fixtures/<exchange>/items.json`. Не выдумывать структуру.
- Покрытие классификаторов — порог 80% реальных заголовков должны
  классифицироваться.
- Новые публичные методы в `TradingBro.Collectors` и
  `TradingBro.Strategies` требуют теста.

## Git-workflow

- Ветвление от `main`: `feat/<short-desc>` или `fix/<short-desc>`.
- Сообщения коммитов — императив настоящего времени на английском:
  `Add Mexc collector`, не `Added Mexc collector`, не `Добавил`.
- PR через `gh pr create`. Title ≤ 70 символов. Body —
  что/зачем/как тестировалось.
- CI должен быть зелёным перед merge. Никаких `--no-verify`.
- NuGet-версии живут в `Directory.Packages.props` (central
  package management). Добавляй пакет именно туда.

## Границы

### Всегда

- Запускай `dotnet build` после каждой правки `.cs` файла.
- При добавлении новой биржи — следуй существующему паттерну из 4
  файлов (см. секцию «Структура проекта»).
- Регистрируй новые коллекторы в
  `CollectorsServiceCollectionExtensions.AddTradingBroCollectors`.
- Cancellation token прокидывай через весь call-stack.

### Сначала уточни

- Правки в `src/TradingBro.Strategies/` — стратегии завязаны на
  live-trading, нужен подтверждённый review.
- Изменения в моделях `Announcement`, `TradeSignal`, `Position` —
  это публичные контракты между подсистемами, требуют согласования.
- Добавление NuGet-пакета — сначала проверь, нет ли уже в
  `Directory.Packages.props`.
- Миграции EF Core — генерируй `dotnet ef migrations add ...`, не
  правь руками снапшот.

### Никогда

- Push в `main` напрямую. Только через PR.
- Force-push куда угодно (`--force`, `--force-with-lease`).
- Правка `appsettings.Local.json`, `appsettings.Development.json`,
  `.env` — они gitignored и содержат секреты.
- Правка `src/TradingBro.Trading/RealTraderService.cs` без явного
  approval — трогает реальный orderflow.
- Отключение `dotnet build` или `dotnet test` (в том числе через
  `<TreatWarningsAsErrors>false</TreatWarningsAsErrors>` или skip-атрибуты).
- Использование Newtonsoft.Json, AutoMapper, MediatR — стек
  сознательно минималистичен.

## Если задача похожа на «добавить биржу» или «починить баг»

Для этих двух частых сценариев в репо есть готовые multi-agent
пайплайны: slash-команды `/add-exchange` и `/fix-bug`. Если
запускаешь один из них — следуй его playbook-у в
`.claude/commands/*.md`, не импровизируй. Обзорная картинка —
`.claude/workflows/README.md`.
