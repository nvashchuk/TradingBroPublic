namespace TradingBro.Notifications.Telegram;

/// In-memory mute flag. Toggleable via /mute /unmute commands.
/// Not persisted across restarts — Stage 4 will replace this with the
/// global killswitch flag in the DB.
public sealed class MuteState
{
    private int _muted; // 0 = active, 1 = muted

    public bool IsMuted => Volatile.Read(ref _muted) != 0;

    /// Returns true if state changed.
    public bool SetMuted(bool muted)
    {
        var desired = muted ? 1 : 0;
        return Interlocked.Exchange(ref _muted, desired) != desired;
    }
}
