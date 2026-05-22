---
description: End-to-end добавление нового сборщика биржевых анонсов (код, тесты, DI, PR). Использование = /add-exchange <Name> <AnnouncementsUrl>
argument-hint: <ExchangeName> <AnnouncementsUrl>
---

Ты — **оркестратор** workflow-а `add-exchange`. Сам ты код не
пишешь. Ты диспатчишь сабагентов и обновляешь `state.json` между
ними. Твоя задача — сделать последовательность устойчивой к сбоям
сабагентов.

## Аргументы

- `$1` (обязательно) = имя биржи (TitleCase, например `Mexc`).
- `$2` (обязательно) = публичный announcements URL, с которого
  начинать.

Если чего-то нет — откажись и объясни.

## Шаг 0 — Инициализация run-а

1. Получи UTC-timestamp: `Bash` →
   `powershell -NoProfile -Command "(Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')"`.
2. Вычисли `slug = lowercase($1)` (имя биржи в нижнем регистре).
3. Сложи `run_id = "add-exchange-<slug>-<timestamp>"`.
   Пример: `add-exchange-mexc-20260521-172054`.
4. Создай папку `.claude/workflows/runs/<run_id>/`.
5. Запиши `.claude/workflows/runs/<run_id>/state.json` с содержимым:

   ```json
   {
     "run_id": "<run_id>",
     "workflow": "add-exchange",
     "inputs": { "exchange_name": "$1", "announcements_url": "$2" },
     "steps": [],
     "human_gates": [],
     "retry_count": {},
     "status": "in_progress"
   }
   ```

6. Создай пустой `.claude/workflows/runs/<run_id>/trace.jsonl`.

Запомни `<run_id>` и путь до папки. Прочитай `state.schema.json`
один раз, чтобы держать форму в голове. Все входы сабагентам идут
через `state.json`, не через прямой чат.

## Pipeline

Для каждого шага ниже: спавни Agent-инструмент с
`subagent_type=<agent>`, собери входной JSON из `state.json`,
после возврата агента — обнови `state.json`:

- Прочитай его, добавь новый объект в массив `steps[]` (поля по
  `state.schema.json`: `agent`, `started_at`, `ended_at`, `status`,
  `inputs_digest`, `outputs`, `validators`, `metrics`).
- Если retry — увеличь `retry_count.<agent>`.
- Запиши state.json обратно.
- Добавь одну компактную JSON-строку в `trace.jsonl` с полями
  `ts`, `agent`, `status`, `latency_ms`.

Между шагами:

- Если у сабагента `status != "ok"` — смотри retry policy ниже.
- Если прошли 2 ретрая без зелёного — ставь `state.status =
  awaiting_human` и стоп.

Последовательность:

1. `exchange-discovery` — даёт ground-truth JSON + field map.
   - Retry policy: если `status == "needs_fixture"` — попроси
     пользователя положить руками `sample.json` в `ground-truth/`
     run-а и перевызови. Автоматического ретрая нет —
     сфабрикованный API это худший failure mode здесь.

2. `exchange-schema` — генерирует `*ApiModels.cs`.
   - Retry: на `status == "schema_mismatch"` — отправь mismatch
     обратно `exchange-discovery` для починки field map.
     Максимум 2 цикла.

3. Запусти `validator` с `step_label=post-schema`.
   - `dotnet build` должен пройти. Если нет — ретрай
     `exchange-schema` со списком build-ошибок. Максимум 1 retry.

4. `exchange-classifier` — генерирует классификатор с
   regex-правилами.
   - На `status == "low_coverage"` — один retry с хинтом:
     `"the previous run classified N/M titles; here are unmatched
     titles: [...]"`. После второго фейла — эскалация человеку.

5. `exchange-collector` — генерирует коллектор.

6. `exchange-parser` — генерирует парсер. Должен использовать те же
   ключи metadata, что и коллектор (передай
   `uses_metadata_keys` из выхода коллектора).

7. `exchange-tests` — генерирует тесты + копирует fixture.

8. `exchange-wireup` — DI-регистрация.

9. Запусти `validator` с `step_label=final`. И build, И test
   должны пройти. Если падает тест — failure уходит обратно
   ПОСЛЕДНЕМУ автору, чей файл назван в тесте (по failure-
   message: `*Classifier*` → exchange-classifier; `*Parser*` →
   exchange-parser).

10. Запусти `critic`. Если `verdict == "request_changes"` —
    отправь каждую issue обратно её owning-агенту. Лимит — 1
    раунд critic-а.

## Human gate

После шага 10 со всем зелёным — ставь
`state.status = "awaiting_human"`. Напечатай пользователю:

```
Run <run_id> ready for review.
  Files changed: <list>
  Build: pass  |  Tests: <N> passed
  Critic verdict: approve
Reply with `approve` or `reject <reason>`.
```

Жди ответа. На `approve` — добавь в `human_gates[]` и иди к шагу 11.
На `reject` — abort и заполни `state.result_summary`.

## Шаг 11 — PR

Спавни `pr-author`. Захвати URL PR-а в `state.result_summary`,
поставь `state.status = "succeeded"`, напечатай URL.

## Таблица обработки сбоев

| Шаг           | Если status=fail дважды                | Действие                |
| ------------- | --------------------------------------- | ----------------------- |
| discovery     | API заблокирован, нет fixture           | Halt, спросить человека |
| schema        | mismatch не уходит                      | Halt, спросить человека |
| classifier    | низкое покрытие                         | Halt, спросить человека |
| collector     | model drift                             | Halt, спросить человека |
| parser        | metadata drift                          | Halt, спросить человека |
| tests         | красные                                 | Маршрут по failing test |
| wireup        | конфликт enum/options                   | Halt, спросить человека |
| validator     | падает build                            | Маршрут по файлу ошибки |
| critic        | request_changes                         | 1 раунд, потом человек  |

Оркестратор никогда не бросает run молча. Каждый halt пишет
`state.result_summary` с причиной и pointer-ами на файлы, которые
нужны человеку.

## Правила использования инструментов для ТЕБЯ (оркестратор)

- `Read` — всё.
- `Edit` / `Write` production-кода (`src/`, `tests/`) — НЕЛЬЗЯ.
  Только сабагенты пишут production. Тебе можно `Edit`/`Write`
  только `.claude/workflows/runs/<id>/state.json` и `trace.jsonl`.
- `Bash` — только для `new-run.ps1` и `dotnet --version` под
  диагностику.
- Сабагентов спавнить через Agent-инструмент с правильным
  `subagent_type` — это даёт каждому собственный изолированный
  контекст.
