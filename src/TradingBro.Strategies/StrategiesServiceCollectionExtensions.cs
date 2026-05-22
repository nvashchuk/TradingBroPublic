using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingBro.Core.Abstractions;
using TradingBro.Strategies.Catalog;
using TradingBro.Strategies.Common;
using TradingBro.Strategies.Debug;
using TradingBro.Strategies.Engine;
using TradingBro.Strategies.Paper;
using TradingBro.Strategies.PriceFeeds;
using TradingBro.Strategies.Strategies.SpotListingShort;

namespace TradingBro.Strategies;

public static class StrategiesServiceCollectionExtensions
{
    public static IServiceCollection AddTradingBroStrategies(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<StrategiesOptions>(configuration.GetSection(StrategiesOptions.SectionName));
        services.Configure<SpotListingShortOptions>(
            configuration.GetSection($"{StrategiesOptions.SectionName}:{nameof(StrategiesOptions.SpotListingShort)}"));

        var opts = configuration.GetSection(StrategiesOptions.SectionName).Get<StrategiesOptions>() ?? new();
        if (!opts.Enabled)
        {
            return services;
        }

        services.AddHttpClient(RestPriceFeed.BybitClient, ConfigureMarketClient);
        services.AddHttpClient(RestPriceFeed.BinanceFuturesClient, ConfigureMarketClient);

        // Catalog: every futures source is registered as IFuturesInstrumentsSource,
        // FuturesInstrumentsRegistry aggregates them.
        services.AddSingleton<BybitFuturesInstrumentsCache>();
        services.AddSingleton<IFuturesInstrumentsSource>(sp => sp.GetRequiredService<BybitFuturesInstrumentsCache>());
        services.AddSingleton<BinanceFuturesInstrumentsCache>();
        services.AddSingleton<IFuturesInstrumentsSource>(sp => sp.GetRequiredService<BinanceFuturesInstrumentsCache>());
        services.AddSingleton<IFuturesInstrumentsRegistry, FuturesInstrumentsRegistry>();

        services.AddSingleton<IPriceFeed, RestPriceFeed>();
        services.AddSingleton<ITradeSignalBus, InMemoryTradeSignalBus>();

        if (opts.SpotListingShort.Enabled)
        {
            services.AddSingleton<IStrategy, SpotListingShortStrategy>();
        }

        services.AddHostedService<StrategyEngine>();
        services.AddHostedService<PaperTraderService>();
        services.AddHostedService<PositionManagementService>();

        // Debug-only: synthetic announcement injection. No-op if config key absent.
        services.AddHostedService<SyntheticAnnouncementInjector>();

        return services;
    }

    private static void ConfigureMarketClient(HttpClient client)
    {
        client.Timeout = TimeSpan.FromSeconds(10);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36");
    }
}
