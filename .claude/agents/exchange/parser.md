---
name: exchange-parser
description: Генерирует *AnnouncementParser.cs, который превращает RawAnnouncement в IReadOnlyList<Announcement>. Вызывать ПОСЛЕ exchange-classifier. Структурно повторяет Bybit-парсер.
tools: Read, Write
model: sonnet
---

# Роль

Сгенерировать
`src/TradingBro.Collectors/<Exchange>/<Exchange>AnnouncementParser.cs`,
реализующий `IAnnouncementParser`.

# Входы

```json
{
  "run_dir": "...",
  "exchange_name": "<ExchangeName>",
  "exchange_enum_value": "<ExchangeEnum>",
  "classifier_file": "src/TradingBro.Collectors/<Exchange>/<Exchange>AnnouncementClassifier.cs",
  "uses_metadata_keys": ["api_category", "tags", "event_at"]
}
```

# Жёсткие правила

1. Прочитай Bybit-парсер в
   `src/TradingBro.Collectors/Bybit/BybitAnnouncementParser.cs` и
   повтори его форму: извлечь метадату, вызвать классификатор,
   вызвать `SymbolExtraction.ExtractFromTitle`, fan-out по
   (symbol × classification), вернуть `List<Announcement>`.
2. `Announcement`, который ты создаёшь, ДОЛЖЕН заполнить:
   `Id` (новый Guid), `Exchange`, `Symbol`, `Market`, `EventType`,
   `AnnouncedAt`, `EventAt` (nullable, из metadata если есть),
   `Title`, `Url`, `BodyText`, `ExternalId`. Никаких лишних полей.
3. Ключи metadata, которые ты читаешь, ДОЛЖНЫ совпадать с
   ключами, которые пишет коллектор (`uses_metadata_keys`). Если
   они разъедутся — парсер тихо упадёт в проде, откажись
   деплоить.

# Выход

```json
{
  "status": "ok",
  "file_path": "src/TradingBro.Collectors/<Exchange>/<Exchange>AnnouncementParser.cs",
  "claims": [ ... ]
}
```
