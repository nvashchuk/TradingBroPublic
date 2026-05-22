namespace TradingBro.Collectors.Common;

public sealed class CollectorsOptions
{
    public const string SectionName = "Collectors";

    public int PollIntervalSeconds { get; set; } = 30;

    public int InitialBackfillHours { get; set; } = 6;

    public ExchangeToggle Binance { get; set; } = new();

    public ExchangeToggle Bybit { get; set; } = new();

    public ExchangeToggle Upbit { get; set; } = new();

    public ExchangeToggle Bithumb { get; set; } = new();

    public ExchangeToggle Okx { get; set; } = new();

    public ExchangeToggle Gate { get; set; } = new();
}

public sealed class ExchangeToggle
{
    public bool Enabled { get; set; } = true;
}
