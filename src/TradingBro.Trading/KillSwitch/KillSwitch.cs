using TradingBro.Core.Abstractions;

namespace TradingBro.Trading.KillSwitch;

/// Thread-safe in-memory kill-switch shared across services.
/// Phase 4: in-memory only (lost on restart). Phase 4.5 will back this with
/// a `kill_switch` table in SQLite so it survives crashes.
public sealed class KillSwitch : IKillSwitch
{
    private int _engaged; // 0 = trading, 1 = halted
    private string _reason = string.Empty;
    private DateTime _engagedAt;

    public bool IsEngaged => Volatile.Read(ref _engaged) != 0;
    public string Reason => _reason;
    public DateTime EngagedAt => _engagedAt;

    public bool Engage(string reason)
    {
        if (Interlocked.Exchange(ref _engaged, 1) != 0)
        {
            return false;
        }
        _reason = reason;
        _engagedAt = DateTime.UtcNow;
        return true;
    }

    public bool Release()
    {
        if (Interlocked.Exchange(ref _engaged, 0) == 0)
        {
            return false;
        }
        _reason = string.Empty;
        _engagedAt = default;
        return true;
    }
}
