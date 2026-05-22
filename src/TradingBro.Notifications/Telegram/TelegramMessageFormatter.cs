using System.Globalization;
using System.Text;
using TradingBro.Core.Abstractions;
using TradingBro.Core.Models;

namespace TradingBro.Notifications.Telegram;

/// Renders NotificationEvent → Telegram HTML string. Pure / no I/O.
///
/// Adding a new event type:
///   1. Add a sealed record in Core/Abstractions/INotifier.cs
///   2. Add a `Type => FormatType(t)` arm in the switch below
///   3. Implement the private formatter
internal static class TelegramMessageFormatter
{
    public static string? Format(NotificationEvent evt) => evt switch
    {
        AnnouncementNotification an => FormatAnnouncement(an.Announcement),
        StrategyStartedNotification ss => FormatStrategyStarted(ss),
        PositionOpenedNotification po => FormatPositionOpened(po.Position),
        PositionClosedNotification pc => FormatPositionClosed(pc),
        StopLossMovedNotification sl => FormatStopLossMoved(sl),
        ErrorNotification en => FormatError(en),
        SystemNotification sn => FormatSystem(sn),
        _ => null,
    };

    private static string FormatAnnouncement(Announcement a)
    {
        var emoji = a.EventType switch
        {
            EventType.Listing => "🆕",
            EventType.Delisting => "⚠️",
            EventType.LaunchpoolAnnounce => "🌊",
            _ => "📰",
        };
        var sb = new StringBuilder();
        sb.Append(emoji).Append(' ').Append("<b>[").Append(a.Exchange).Append("]</b> ");
        sb.Append(a.EventType.ToString().ToUpperInvariant());
        sb.Append(": <code>").Append(Escape(a.Symbol)).Append("</code> (").Append(a.Market).Append(")\n");
        sb.Append(Escape(a.Title)).Append('\n');
        sb.Append("Announced: ").Append(FormatUtc(a.AnnouncedAt));
        if (a.EventAt is not null)
        {
            sb.Append("\nEvent: ").Append(FormatUtc(a.EventAt.Value));
        }
        sb.Append("\n<a href=\"").Append(Escape(a.Url)).Append("\">link</a>");
        return sb.ToString();
    }

    private static string FormatStrategyStarted(StrategyStartedNotification s)
    {
        var sb = new StringBuilder();
        sb.Append("🎯 <b>Strategy started</b>: <code>").Append(Escape(s.StrategyName)).Append("</code>\n");
        sb.Append(s.Side == Side.Long ? "📈 LONG" : "📉 SHORT");
        sb.Append(" <code>").Append(Escape(s.Symbol)).Append("</code> on ").Append(s.Exchange);
        sb.Append(" ").Append(s.Market).Append('\n');
        sb.Append("Quote: ").Append(s.QuoteAmount.ToString("0.##", CultureInfo.InvariantCulture)).Append(" USDT");
        if (!string.IsNullOrEmpty(s.Note))
        {
            sb.Append('\n').Append(Escape(s.Note));
        }
        return sb.ToString();
    }

    private static string FormatPositionOpened(Position p)
    {
        var sb = new StringBuilder();
        sb.Append(p.IsPaper ? "📝 <b>Paper OPEN</b>" : "✅ <b>OPEN</b>");
        sb.Append(' ').Append(p.Side == Side.Long ? "LONG" : "SHORT");
        sb.Append(" <code>").Append(Escape(p.Symbol)).Append("</code> @ ");
        sb.Append(p.EntryPrice.ToString("0.########", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("Qty: ").Append(p.Quantity.ToString("0.########", CultureInfo.InvariantCulture));
        if (p.StopLossPrice is not null)
        {
            sb.Append("  SL: ").Append(p.StopLossPrice.Value.ToString("0.########", CultureInfo.InvariantCulture));
        }
        if (p.TakeProfitPrice is not null)
        {
            sb.Append("  TP: ").Append(p.TakeProfitPrice.Value.ToString("0.########", CultureInfo.InvariantCulture));
        }
        sb.Append("\nStrategy: <code>").Append(Escape(p.StrategyName)).Append("</code>");
        return sb.ToString();
    }

    private static string FormatPositionClosed(PositionClosedNotification c)
    {
        var p = c.Position;
        var emoji = c.RealizedPnlUsd switch
        {
            null => "⚪",
            >= 0 => "🟢",
            < 0 => "🔴",
        };
        var sb = new StringBuilder();
        sb.Append(emoji).Append(' ').Append(p.IsPaper ? "<b>Paper CLOSE</b>" : "<b>CLOSE</b>");
        sb.Append(' ').Append(p.Side == Side.Long ? "LONG" : "SHORT");
        sb.Append(" <code>").Append(Escape(p.Symbol)).Append("</code>");
        if (p.ExitPrice is not null)
        {
            sb.Append(" @ ").Append(p.ExitPrice.Value.ToString("0.########", CultureInfo.InvariantCulture));
        }
        if (c.RealizedPnlUsd is not null)
        {
            sb.Append("\nPnL: ").Append(c.RealizedPnlUsd.Value.ToString("+0.##;-0.##", CultureInfo.InvariantCulture)).Append(" USDT");
            if (c.RealizedPnlPct is not null)
            {
                sb.Append(" (").Append(c.RealizedPnlPct.Value.ToString("+0.##;-0.##", CultureInfo.InvariantCulture)).Append("%)");
            }
        }
        if (!string.IsNullOrEmpty(c.Reason))
        {
            sb.Append("\nReason: ").Append(Escape(c.Reason));
        }
        return sb.ToString();
    }

    private static string FormatStopLossMoved(StopLossMovedNotification s)
    {
        var sb = new StringBuilder();
        sb.Append("🛡️ <b>SL moved</b> <code>").Append(Escape(s.Position.Symbol)).Append("</code>: ");
        sb.Append(s.OldStopPrice.ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(" → ");
        sb.Append(s.NewStopPrice.ToString("0.########", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    private static string FormatError(ErrorNotification e)
    {
        var sb = new StringBuilder();
        sb.Append("❌ <b>Error</b>: ").Append(Escape(e.Message));
        if (!string.IsNullOrEmpty(e.Detail))
        {
            sb.Append("\n<code>").Append(Escape(e.Detail!)).Append("</code>");
        }
        return sb.ToString();
    }

    private static string FormatSystem(SystemNotification s)
    {
        var sb = new StringBuilder();
        sb.Append("ℹ️ <b>System</b>: ").Append(Escape(s.Message));
        if (!string.IsNullOrEmpty(s.Detail))
        {
            sb.Append("\n<code>").Append(Escape(s.Detail!)).Append("</code>");
        }
        return sb.ToString();
    }

    private static string FormatUtc(DateTime utc) =>
        utc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private static string Escape(string s) =>
        s.Replace("&", "&amp;", StringComparison.Ordinal)
         .Replace("<", "&lt;", StringComparison.Ordinal)
         .Replace(">", "&gt;", StringComparison.Ordinal);
}
