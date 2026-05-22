namespace TradingBro.Core.Models;

public class TradeSignal
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AnnouncementId { get; set; }

    public Exchange TargetExchange { get; set; }

    public string Symbol { get; set; } = string.Empty;

    public Market Market { get; set; }

    public Side Side { get; set; }

    public decimal QuoteAmount { get; set; }

    public decimal? StopLossPct { get; set; }

    public decimal? TakeProfitPct { get; set; }

    /// 1 = no leverage. Strategy-controlled.
    public int Leverage { get; set; } = 1;

    public MarginMode MarginMode { get; set; } = MarginMode.Isolated;

    public string StrategyName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
