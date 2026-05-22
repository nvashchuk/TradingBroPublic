using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TradingBro.Core.Abstractions;
using TradingBro.Data;

namespace TradingBro.Notifications.Telegram;

/// Long-polls Telegram for messages and handles bot commands:
///   /status, /recent [exchange], /mute, /unmute, /strategies, /halt, /resume
internal sealed class TelegramCommandService(
    ITelegramBotClient botClient,
    IServiceProvider services,
    IServiceScopeFactory scopeFactory,
    IOptions<TelegramOptions> options,
    MuteState muteState,
    ILogger<TelegramCommandService> logger)
    : BackgroundService
{
    private static readonly DateTime StartedAt = DateTime.UtcNow;

    private readonly TelegramOptions _options = options.Value;
    private readonly HashSet<long> _allowedChats = new(options.Value.ChatIds);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.CommandsEnabled)
        {
            logger.LogInformation("TelegramCommandService disabled via config");
            return;
        }

        logger.LogInformation("TelegramCommandService started");

        // Drop pending updates from previous session.
        var offset = 0;
        try
        {
            var pending = await botClient.GetUpdates(offset: -1, limit: 1, cancellationToken: stoppingToken);
            if (pending.Length > 0)
            {
                offset = pending[^1].Id + 1;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch initial Telegram offset");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            Update[] updates;
            try
            {
                updates = await botClient.GetUpdates(
                    offset: offset,
                    timeout: 30,
                    allowedUpdates: new[] { UpdateType.Message },
                    cancellationToken: stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Telegram GetUpdates failed — backing off 5s");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }

            foreach (var update in updates)
            {
                offset = update.Id + 1;
                try
                {
                    await HandleUpdateAsync(update, stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to handle update {UpdateId}", update.Id);
                }
            }
        }
    }

    private async Task HandleUpdateAsync(Update update, CancellationToken ct)
    {
        var msg = update.Message;
        if (msg?.Text is null || msg.Chat is null)
        {
            return;
        }

        // Authorize: only chats from config can issue commands.
        // Closed by default — empty allowlist means nobody is authorized.
        if (!_allowedChats.Contains(msg.Chat.Id))
        {
            logger.LogWarning("Ignoring command from unauthorized chat {ChatId} (user @{Username})",
                msg.Chat.Id, msg.From?.Username ?? "?");
            return;
        }

        var (command, args) = ParseCommand(msg.Text);
        if (command is null)
        {
            return;
        }

        var reply = command switch
        {
            "/start" or "/help" => Help(),
            "/status" => Status(),
            "/recent" => await Recent(args, ct),
            "/mute" => Mute(true),
            "/unmute" => Mute(false),
            "/strategies" => Strategies(),
            "/halt" => Halt(args),
            "/resume" => Resume(),
            _ => null,
        };

        if (reply is not null)
        {
            await botClient.SendMessage(msg.Chat.Id, reply, ParseMode.Html, cancellationToken: ct);
        }
    }

    private static (string? command, string args) ParseCommand(string text)
    {
        var trimmed = text.TrimStart();
        if (!trimmed.StartsWith('/'))
        {
            return (null, string.Empty);
        }
        var spaceIdx = trimmed.IndexOf(' ');
        var head = spaceIdx < 0 ? trimmed : trimmed[..spaceIdx];
        var args = spaceIdx < 0 ? string.Empty : trimmed[(spaceIdx + 1)..].Trim();
        // Strip @botname suffix Telegram appends in group chats: /status@MyBot
        var atIdx = head.IndexOf('@');
        if (atIdx > 0)
        {
            head = head[..atIdx];
        }
        return (head.ToLowerInvariant(), args);
    }

    private static string Help() =>
        "<b>TradingBro commands</b>\n" +
        "/status — uptime, pipeline &amp; killswitch state\n" +
        "/recent [exchange] — last 10 announcements\n" +
        "/mute — silence notifications\n" +
        "/unmute — resume notifications\n" +
        "/strategies — list active strategies\n" +
        "/halt [reason] — stop opening new positions (existing kept running)\n" +
        "/resume — resume trading after /halt";

    private string Status()
    {
        var uptime = DateTime.UtcNow - StartedAt;
        var muted = muteState.IsMuted ? "yes" : "no";
        var sb = new StringBuilder()
            .Append("📊 <b>Status</b>\n")
            .Append("Uptime: ").Append((int)uptime.TotalHours).Append("h ").Append(uptime.Minutes).Append("m\n")
            .Append("Started: ").Append(StartedAt.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)).Append('\n')
            .Append("Muted: ").Append(muted);

        var ks = services.GetService<IKillSwitch>();
        if (ks is not null)
        {
            sb.Append("\nTrading: ").Append(ks.IsEngaged ? $"🛑 HALTED ({EscapeHtml(ks.Reason)})" : "✅ active");
        }
        return sb.ToString();
    }

    private async Task<string> Recent(string args, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TradingBroDbContext>();

        var q = db.Announcements.AsQueryable();
        if (!string.IsNullOrWhiteSpace(args)
            && Enum.TryParse<Core.Models.Exchange>(args, ignoreCase: true, out var ex))
        {
            q = q.Where(x => x.Exchange == ex);
        }

        var rows = await q.OrderByDescending(x => x.AnnouncedAt).Take(10).ToListAsync(ct);
        if (rows.Count == 0)
        {
            return "No announcements yet.";
        }

        var sb = new StringBuilder("🗒️ <b>Recent</b>\n");
        foreach (var a in rows)
        {
            sb.Append('[').Append(a.Exchange).Append("] ");
            sb.Append("<code>").Append(EscapeHtml(a.Symbol)).Append("</code> ");
            sb.Append(a.Market).Append(' ').Append(a.EventType).Append(' ');
            sb.Append(a.AnnouncedAt.ToString("MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)).Append('\n');
        }
        return sb.ToString();
    }

    private string Mute(bool muted)
    {
        var changed = muteState.SetMuted(muted);
        var verb = muted ? "muted" : "unmuted";
        return changed ? $"🔕 Notifications {verb}." : $"Already {verb}.";
    }

    private static string Strategies() =>
        "No strategies registered yet. (Stage 3)";

    private string Halt(string args)
    {
        var ks = services.GetService<IKillSwitch>();
        if (ks is null)
        {
            return "Killswitch unavailable (Trading module disabled).";
        }
        var reason = string.IsNullOrWhiteSpace(args) ? "manual via /halt" : args;
        return ks.Engage(reason)
            ? $"🛑 Trading halted: {EscapeHtml(reason)}"
            : $"Already halted ({EscapeHtml(ks.Reason)}).";
    }

    private string Resume()
    {
        var ks = services.GetService<IKillSwitch>();
        if (ks is null)
        {
            return "Killswitch unavailable (Trading module disabled).";
        }
        return ks.Release() ? "✅ Trading resumed." : "Already running.";
    }

    private static string EscapeHtml(string s) =>
        s.Replace("&", "&amp;", StringComparison.Ordinal)
         .Replace("<", "&lt;", StringComparison.Ordinal)
         .Replace(">", "&gt;", StringComparison.Ordinal);
}
