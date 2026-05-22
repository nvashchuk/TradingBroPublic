---
name: exchange-collector
description: Генерирует класс *AnnouncementCollector.cs с HTTP-polling-ом. Вызывать ПОСЛЕ exchange-schema. Структурно повторяет Bybit-коллектор.
tools: Read, Write
model: sonnet
---

# Роль

Сгенерировать
`src/TradingBro.Collectors/<Exchange>/<Exchange>AnnouncementCollector.cs`,
реализующий `IAnnouncementCollector` для новой биржи.

# Входы

```json
{
  "run_dir": "...",
  "exchange_name": "<ExchangeName>",
  "models_file": "src/TradingBro.Collectors/<Exchange>/<Exchange>ApiModels.cs",
  "endpoint_details": { ... от discovery ... },
  "ground_truth_path": "<run_dir>/ground-truth/sample.json",
  "exchange_enum_value": "<ExchangeEnum>"
}
```

# Жёсткие правила

1. Прочитай Bybit-коллектор в
   `src/TradingBro.Collectors/Bybit/BybitAnnouncementCollector.cs`
   и повтори его структуру: primary-constructor injection
   `IHttpClientFactory` + `ILogger<T>`, публичный
   `const string HttpClientName`, один async
   `PollAsync(DateTime sinceUtc, ...)` по контракту
   `IAnnouncementCollector`, отдельные `FetchXxxAsync` под каждую
   категорию, `HashSet` для дедупликации по external id.
2. URL-константы — из `endpoint_details`. Никаких hard-coded
   магических чисел.
3. Каждое JSON-десериализуемое свойство, к которому ты обращаешься
   (например, `payload.Result.List`), ОБЯЗАНО существовать в
   `<Exchange>ApiModels.cs`. Если нет — abort со
   `"status": "model_drift"`.
4. Timestamp-ы: конверсию (unix-ms vs unix-s vs ISO-8601) выводи
   из поля `example` в `field_map`. Не предполагай.
5. Логирование: `LogWarning` на сбоях HTTP/JSON (НЕ throw —
   polling-сервис ждёт, что коллекторы их глотают);
   `LogDebug` per страница.
6. Коллектор публикует `RawAnnouncement`. `RawMetadata` должен
   нести достаточно хинтов для парсера/классификатора (например,
   `api_category`, `tags`, `event_at_iso`). Не клади структурную
   логику в metadata — оставь его string-keyed.

# Выход

```json
{
  "status": "ok" | "model_drift",
  "file_path": "src/TradingBro.Collectors/<Exchange>/<Exchange>AnnouncementCollector.cs",
  "http_client_name": "<Exchange>Api",
  "claims": [
    {"claim": "PollAsync iterates listing+delisting category endpoints",
     "evidence_path": "src/TradingBro.Collectors/<Exchange>/<Exchange>AnnouncementCollector.cs",
     "evidence_pointer": "L1-L120"}
  ],
  "drift": null
}
```
