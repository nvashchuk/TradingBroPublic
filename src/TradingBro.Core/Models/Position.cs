namespace TradingBro.Core.Models;

public enum PositionStatus
{
    Open = 1,
    Closed = 2,
    Failed = 3,
}

public class Position
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TradeSignalId { get; set; }

    public string StrategyName { get; set; } = string.Empty;

    public Exchange Exchange { get; set; }

    public string Symbol { get; set; } = string.Empty;

    public Market Market { get; set; }

    public Side Side { get; set; }

    public decimal EntryPrice { get; set; }

    public decimal Quantity { get; set; }

    public decimal? StopLossPrice { get; set; }

    public decimal? TakeProfitPrice { get; set; }

    public int Leverage { get; set; } = 1;

    public MarginMode MarginMode { get; set; } = MarginMode.Isolated;

    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ClosedAt { get; set; }

    public decimal? ExitPrice { get; set; }

    public decimal? RealizedPnlUsd { get; set; }

    public PositionStatus Status { get; set; } = PositionStatus.Open;

    public string? ExchangeOrderId { get; set; }

    public bool IsPaper { get; set; }
}
