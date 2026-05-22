namespace TradingBro.Core.Abstractions;

/// Cross-cutting kill-switch read by RealTrader and toggled by /halt /resume.
public interface IKillSwitch
{
    bool IsEngaged { get; }

    string Reason { get; }

    DateTime EngagedAt { get; }

    bool Engage(string reason);

    bool Release();
}
