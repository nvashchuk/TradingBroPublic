using Microsoft.EntityFrameworkCore;
using TradingBro.Core.Models;

namespace TradingBro.Data;

public sealed class TradingBroDbContext(DbContextOptions<TradingBroDbContext> options) : DbContext(options)
{
    public DbSet<Announcement> Announcements => Set<Announcement>();

    public DbSet<TradeSignal> TradeSignals => Set<TradeSignal>();

    public DbSet<Position> Positions => Set<Position>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TradingBroDbContext).Assembly);
    }
}
