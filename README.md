# TradingBro.NET

.NET 10 listing-sniper for crypto exchanges. Polls public
announcements endpoints (Binance, Bybit, Upbit, Bithumb), classifies
listing/delisting events, and routes trade signals through the
configured strategies (paper or real).

## Layout

```
src/
  TradingBro.Core           — shared abstractions and DTOs
  TradingBro.Collectors     — per-exchange announcement collectors
  TradingBro.Data           — EF Core / SQLite persistence
  TradingBro.Strategies     — strategy engine + SpotListingShort
  TradingBro.Trading        — exchange clients (Binance live + demo)
  TradingBro.Notifications  — Telegram bot
  TradingBro.Worker         — hosted service entry point
tests/
  TradingBro.Collectors.Tests
  TradingBro.Strategies.Tests
```

## Running

```
dotnet build TradingBro.slnx
dotnet test  TradingBro.slnx
dotnet run --project src/TradingBro.Worker
```

Local configuration goes in `appsettings.Local.json` (gitignored).

## Internal docs

- `AGENTS.md` — repo conventions for AI coding agents (build/test
  commands, code style, boundaries). Short, intended for agents.
- `.claude/` — slash commands (`/add-exchange`, `/fix-bug`),
  subagent definitions, state schema, PostToolUse build hook.