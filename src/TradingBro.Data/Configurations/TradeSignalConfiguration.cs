using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBro.Core.Models;

namespace TradingBro.Data.Configurations;

public sealed class TradeSignalConfiguration : IEntityTypeConfiguration<TradeSignal>
{
    public void Configure(EntityTypeBuilder<TradeSignal> b)
    {
        b.ToTable("trade_signals");
        b.HasKey(x => x.Id);

        b.Property(x => x.Symbol).HasMaxLength(32).IsRequired();
        b.Property(x => x.StrategyName).HasMaxLength(64).IsRequired();
        b.Property(x => x.TargetExchange).HasConversion<int>();
        b.Property(x => x.Market).HasConversion<int>();
        b.Property(x => x.Side).HasConversion<int>();
        b.Property(x => x.MarginMode).HasConversion<int>();

        b.Property(x => x.QuoteAmount).HasPrecision(18, 8);
        b.Property(x => x.StopLossPct).HasPrecision(10, 6);
        b.Property(x => x.TakeProfitPct).HasPrecision(10, 6);

        b.HasIndex(x => x.AnnouncementId).HasDatabaseName("ix_signals_announcement");
        b.HasIndex(x => new { x.StrategyName, x.CreatedAt }).HasDatabaseName("ix_signals_strategy");
    }
}
