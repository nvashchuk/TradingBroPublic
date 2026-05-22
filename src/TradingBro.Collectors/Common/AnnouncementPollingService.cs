using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingBro.Core.Abstractions;
using TradingBro.Core.Models;
using TradingBro.Data;

namespace TradingBro.Collectors.Common;

public sealed class AnnouncementPollingService(
    IServiceScopeFactory scopeFactory,
    IOptions<CollectorsOptions> options,
    ILogger<AnnouncementPollingService> logger)
    : BackgroundService
{
    private readonly Dictionary<Exchange, DateTime> _cursors = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        var interval = TimeSpan.FromSeconds(Math.Max(5, opts.PollIntervalSeconds));

        await InitializeCursorsAsync(opts, stoppingToken);

        logger.LogInformation(
            "AnnouncementPollingService started. Interval={Interval}s, cursors={Cursors}",
            interval.TotalSeconds,
            string.Join(", ", _cursors.Select(c => $"{c.Key}@{c.Value:O}")));

        // No grace period needed: bus subscribers register their channels in
        // their constructors (eager-init Subscribe), guaranteed to be in place
        // by the time we publish here.

        using var timer = new PeriodicTimer(interval);
        do
        {
            await TickAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task InitializeCursorsAsync(CollectorsOptions opts, CancellationToken ct)
    {
        var fallback = DateTime.UtcNow - TimeSpan.FromHours(Math.Max(0, opts.InitialBackfillHours));
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TradingBroDbContext>();
        var collectors = scope.ServiceProvider.GetServices<IAnnouncementCollector>();

        foreach (var collector in collectors)
        {
            var latest = await db.Announcements
                .Where(x => x.Exchange == collector.Exchange)
                .OrderByDescending(x => x.AnnouncedAt)
                .Select(x => (DateTime?)x.AnnouncedAt)
                .FirstOrDefaultAsync(ct);
            _cursors[collector.Exchange] = latest ?? fallback;
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<TradingBroDbContext>();
        var bus = sp.GetRequiredService<IAnnouncementBus>();
        var collectors = sp.GetServices<IAnnouncementCollector>().ToArray();
        var parsers = sp.GetServices<IAnnouncementParser>().ToDictionary(p => p.Exchange);

        var pollTasks = collectors.Select(c => PollOneAsync(c, ct)).ToArray();
        var rawBatches = await Task.WhenAll(pollTasks);

        for (var i = 0; i < collectors.Length; i++)
        {
            var collector = collectors[i];
            var raws = rawBatches[i];
            if (raws is null || raws.Count == 0)
            {
                continue;
            }
            if (!parsers.TryGetValue(collector.Exchange, out var parser))
            {
                logger.LogWarning("No parser registered for {Exchange}, dropping {Count} raw announcements", collector.Exchange, raws.Count);
                continue;
            }

            var freshlyAdded = new List<Announcement>();
            var maxAnnouncedAt = _cursors[collector.Exchange];

            foreach (var raw in raws)
            {
                if (raw.AnnouncedAt > maxAnnouncedAt)
                {
                    maxAnnouncedAt = raw.AnnouncedAt;
                }

                foreach (var announcement in parser.Parse(raw))
                {
                    var exists = await db.Announcements.AnyAsync(
                        x => x.Exchange == announcement.Exchange
                          && x.Symbol == announcement.Symbol
                          && x.Market == announcement.Market
                          && x.EventType == announcement.EventType
                          && x.AnnouncedAt == announcement.AnnouncedAt,
                        ct);
                    if (exists)
                    {
                        continue;
                    }
                    db.Announcements.Add(announcement);
                    freshlyAdded.Add(announcement);
                }
            }

            if (freshlyAdded.Count > 0)
            {
                await db.SaveChangesAsync(ct);
            }
            _cursors[collector.Exchange] = maxAnnouncedAt;

            // Publish only what we actually inserted, after the DB commit
            // so subscribers can safely query.
            foreach (var announcement in freshlyAdded)
            {
                await bus.PublishAsync(announcement, ct);
            }

            if (freshlyAdded.Count > 0)
            {
                logger.LogInformation("{Exchange}: {New} new announcements persisted", collector.Exchange, freshlyAdded.Count);
            }
        }
    }

    private async Task<IReadOnlyList<RawAnnouncement>?> PollOneAsync(IAnnouncementCollector collector, CancellationToken ct)
    {
        try
        {
            var since = _cursors[collector.Exchange];
            var raws = await collector.PollAsync(since, ct);
            logger.LogDebug("{Exchange}: polled {Count} raw items since {Since:O}", collector.Exchange, raws.Count, since);
            return raws;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Collector {Exchange} threw during poll", collector.Exchange);
            return null;
        }
    }
}
