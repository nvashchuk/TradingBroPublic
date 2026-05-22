using TradingBro.Core.Models;

namespace TradingBro.Core.Abstractions;

public interface IStrategy
{
    string Name { get; }

    TimeSpan ManagementInterval { get; }

    Task<TradeSignal?> OnAnnouncementAsync(Announcement announcement, CancellationToken ct);

    /// Called periodically by PositionManagementService for every open position
    /// originated by this strategy. The strategy may:
    ///   * mutate Position.StopLossPrice / TakeProfitPrice (trailing stop, etc.)
    ///   * close the position directly by setting Status=Closed, ExitPrice, ClosedAt
    /// `currentPrice` is the latest mark price from the configured IPriceFeed.
    Task ManagePositionAsync(Position position, decimal currentPrice, CancellationToken ct);
}
