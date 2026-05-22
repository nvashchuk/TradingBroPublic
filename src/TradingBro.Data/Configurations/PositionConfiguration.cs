using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBro.Core.Models;

namespace TradingBro.Data.Configurations;

public sealed class PositionConfiguration : IEntityTypeConfiguration<Position>
{
    public void Configure(EntityTypeBuilder<Position> b)
    {
        b.ToTable("positions");
        b.HasKey(x => x.Id);

        b.Property(x => x.Symbol).HasMaxLength(32).IsRequired();
        b.Property(x => x.StrategyName).HasMaxLength(64).IsRequired();
        b.Property(x => x.ExchangeOrderId).HasMaxLength(64);

        b.Property(x => x.Exchange).HasConversion<int>();
        b.Property(x => x.Market).HasConversion<int>();
        b.Property(x => x.Side).HasConversion<int>();
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.MarginMode).HasConversion<int>();

        b.Property(x => x.EntryPrice).HasPrecision(28, 12);
        b.Property(x => x.ExitPrice).HasPrecision(28, 12);
        b.Property(x => x.Quantity).HasPrecision(28, 12);
        b.Property(x => x.StopLossPrice).HasPrecision(28, 12);
        b.Property(x => x.TakeProfitPrice).HasPrecision(28, 12);
        b.Property(x => x.RealizedPnlUsd).HasPrecision(18, 8);

        b.HasIndex(x => x.Status).HasDatabaseName("ix_positions_status");
        b.HasIndex(x => new { x.Exchange, x.Symbol, x.Status }).HasDatabaseName("ix_positions_symbol_status");
    }
}
