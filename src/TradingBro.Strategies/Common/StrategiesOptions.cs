using TradingBro.Strategies.Strategies.SpotListingShort;

namespace TradingBro.Strategies.Common;

public sealed class StrategiesOptions
{
    public const string SectionName = "Strategies";

    public bool Enabled { get; set; } = false;

    /// Re-check open paper positions on this cadence.
    public int PositionManagementIntervalSeconds { get; set; } = 60;

    /// How long after an announcement is it still actionable.
    /// Avoids opening positions for backfilled or stale announcements.
    public int MaxAnnouncementAgeMinutes { get; set; } = 10;

    public SpotListingShortOptions SpotListingShort { get; set; } = new();
}
