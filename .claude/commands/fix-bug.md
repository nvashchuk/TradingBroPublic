---
description: Багрепорт → падающий репро-тест → минимальный патч → зелёные тесты → PR. Использование = /fix-bug <BugReportPath>
argument-hint: <BugReportPath>
---

Ты — **оркестратор** workflow-а `fix-bug`.

## Аргументы

- `$1` (обязательно) = путь к `.md` багрепорту. Репорт ДОЛЖЕН
  содержать:
  - `## Observed` (что происходит)
  - `## Expected` (что должно происходить)
  - `## Reproducer` (шаги или входы)
  - Опционально `## Suspect files`

Если путь не резолвится или секции отсутствуют — откажись. Этот
workflow — точный-вход-или-ничего. Размытый репорт даст размытый
фикс.

## Шаг 0 — Инициализация run-а

1. Получи UTC-timestamp: `Bash` →
   `powershell -NoProfile -Command "(Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')"`.
2. Вычисли `slug` из `$1`: возьми basename файла без расширения `.md`,
   приведи к нижнему регистру.
   Пример: `$1 = "bugs/symbol-extraction.md"` → `slug = "symbol-extraction"`.
3. Сложи `run_id = "fix-bug-<slug>-<timestamp>"`.
   Пример: `fix-bug-symbol-extraction-20260521-172054`.
4. Создай папку `.claude/workflows/runs/<run_id>/`.
5. Запиши `.claude/workflows/runs/<run_id>/state.json` с содержимым:

   ```json
   {
     "run_id": "<run_id>",
     "workflow": "fix-bug",
     "inputs": { "bug_report_path": "$1" },
     "steps": [],
     "human_gates": [],
     "retry_count": {},
     "status": "in_progress"
   }
   ```

6. Создай пустой `.claude/workflows/runs/<run_id>/trace.jsonl`.

## Pipeline

После возврата каждого сабагента — обнови `state.json`: добавь
новый объект в `steps[]` (поля по `state.schema.json`), при retry
увеличь `retry_count.<agent>`, добавь строку в `trace.jsonl`.

1. **bug-repro-author** — пишет падающий xUnit-тест, который
   ассертит `Expected`-поведение на входах из `Reproducer`. Должен
   убедиться, что тест падает на HEAD. Если
   `status == "cannot_repro"` или `"bug_already_fixed"` — halt с
   ясным сообщением. К патчу не переходим.

2. **bug-locator** — read-only расследование. Возвращает
   `root_cause.{file, line_range, confidence}`. Если
   `confidence == "low"` — спроси человека подтвердить файл,
   прежде чем дать patch-агенту его трогать. (Low-confidence
   правки в трейдинговой системе — это failure mode, из которого
   автоматически не выкарабкаться.)

3. **bug-patch-author** — минимальный edit. В `forbidden_to_edit`
   список входит файл репро-теста. После правки агент сам
   запускает целевой тест + полный suite.

4. **validator** с `step_label=post-patch`. Полный build + полный
   test. Если что-то регрессировало — эскалируй патч обратно
   patch-агенту со списком регрессий. Максимум 1 retry.

5. **critic** — обязан верифицировать, что патч реально адресует
   root cause, который назвал locator, а не симптом. Конкретно:
   прочитать `root_cause.explanation` и грепнуть diff на
   evidence того, что underlying cause устранён. Если
   `request_changes` — halt человеку.

## Human gate

То же, что у add-exchange: после зелёного + approve critic
ставь `status = awaiting_human`. Человек смотрит diff +
failure-then-pass лог теста + гипотезу locator-а. Главный вопрос
к человеку — *«это правильный root cause или симптом?»*. Агент
уверенно на это не отвечает.

## Шаг 6 — PR

`pr-author`. Тело PR ОБЯЗАНО содержать:
- Ссылку на багрепорт
- Имя репро-теста
- `root_cause.explanation` от locator-а
- Diff
- Финальный результат validator-а

## Почему такая форма

- Репро-тест пишется ДО патча. Это единственный способ не
  дать patch-агенту убедить самого себя, что баг исправлен,
  когда он просто что-то переименовал.
- Locator — read-only. Объединение locate+patch в одного агента
  почти всегда расширяет scope («раз уж я здесь, заодно фикснул…»).
- Critic проверяет root cause vs симптом. Это место, где
  большинство AI-сгенерированных фиксов тихо падают в проде.
