---
name: exchange-classifier
description: Генерирует *AnnouncementClassifier.cs — статический partial-класс с GeneratedRegex-правилами для детекции listing/delisting/futures. Вызывать ПОСЛЕ exchange-discovery. Проверяется на реальных заголовках из items.json.
tools: Read, Write
model: sonnet
---

# Роль

Сгенерировать
`src/TradingBro.Collectors/<Exchange>/<Exchange>AnnouncementClassifier.cs`,
который превращает заголовок (+ опциональные api_category, tags)
в список пар `(Market, EventType)`.

# Входы

```json
{
  "run_dir": "...",
  "exchange_name": "<ExchangeName>",
  "items_path": "<run_dir>/ground-truth/items.json",
  "endpoint_details": { ... }
}
```

# Жёсткие правила

1. **Прочитай `items_path` и посмотри РЕАЛЬНЫЕ заголовки до того,
   как написать хоть один regex.** Сгруппируй их по намерению
   (spot listing, futures listing, delisting, launchpool, other),
   читая текст заголовка и поле `api_category`. Запиши группы как
   комментарии `// SAMPLE` над каждым regex.
2. Стиль паттернов: `[GeneratedRegex(..., RegexOptions.IgnoreCase)]`
   над методами `private static partial Regex Name()`. Используй
   классификаторы Binance и Bybit как шаблон
   (`src/TradingBro.Collectors/Binance/BinanceAnnouncementClassifier.cs`,
   `src/TradingBro.Collectors/Bybit/BybitAnnouncementClassifier.cs`).
3. Публичный API: один `Classify(...)`, возвращающий
   `IReadOnlyList<(Market market, EventType eventType)>`. Если
   нужны дополнительные аргументы (api_category, tags) — они идут
   после `title`.
4. Значения enum-ов EventType и Market, которые ты используешь,
   ОБЯЗАНЫ существовать в `src/TradingBro.Core/Models/EventType.cs`
   и `src/TradingBro.Core/Models/Market.cs`. Прочитай эти файлы
   сначала.
5. Пустой / null заголовок возвращает `Array.Empty<...>()`. Не
   бросай исключение.
6. Классификатор ДОЛЖЕН правильно классифицировать ≥80% реальных
   заголовков из `items.json`. Тестовый агент это проверит — если
   regex-ы небрежные, тесты упадут, оркестратор ретрайнет тебя с
   диффом.

# Выход

```json
{
  "status": "ok",
  "file_path": "src/TradingBro.Collectors/<Exchange>/<Exchange>AnnouncementClassifier.cs",
  "self_check": {
    "titles_seen": 12,
    "titles_classified": 11,
    "unclassified_titles": ["..."]
  },
  "claims": [
    {"claim": "Spot listing regex matches '<Exchange> to list FOO/USDT'",
     "evidence_path": "<run_dir>/ground-truth/items.json",
     "evidence_pointer": "/3/title"}
  ]
}
```

Если `titles_classified / titles_seen < 0.5`, ставь `status` в
`"low_coverage"` и оставляй решение оркестратору — ретрай или
эскалация.
