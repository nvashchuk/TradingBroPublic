using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TradingBro.Data;

/// Used by `dotnet ef` at design-time. Production wiring lives in the Worker.
public sealed class TradingBroDbContextFactory : IDesignTimeDbContextFactory<TradingBroDbContext>
{
    public TradingBroDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TradingBroDbContext>()
            .UseSqlite("Data Source=./data/tradingbro.db")
            .Options;
        return new TradingBroDbContext(options);
    }
}
