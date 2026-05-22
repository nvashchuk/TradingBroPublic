using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingBro.Collectors.Binance;
using TradingBro.Collectors.Bithumb;
using TradingBro.Collectors.Bybit;
using TradingBro.Collectors.Common;
using TradingBro.Collectors.Upbit;
using TradingBro.Core.Abstractions;

namespace TradingBro.Collectors;

public static class CollectorsServiceCollectionExtensions
{
    public static IServiceCollection AddTradingBroCollectors(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CollectorsOptions>(configuration.GetSection(CollectorsOptions.SectionName));

        services.AddSingleton<IAnnouncementBus, InMemoryAnnouncementBus>();

        var opts = configuration.GetSection(CollectorsOptions.SectionName).Get<CollectorsOptions>() ?? new CollectorsOptions();

        if (opts.Binance.Enabled)
        {
            services.AddHttpClient(BinanceAnnouncementCollector.HttpClientName, ConfigureBrowserClient);
            services.AddSingleton<IAnnouncementCollector, BinanceAnnouncementCollector>();
            services.AddSingleton<IAnnouncementParser, BinanceAnnouncementParser>();
        }

        if (opts.Bybit.Enabled)
        {
            services.AddHttpClient(BybitAnnouncementCollector.HttpClientName, ConfigureJsonClient);
            services.AddSingleton<IAnnouncementCollector, BybitAnnouncementCollector>();
            services.AddSingleton<IAnnouncementParser, BybitAnnouncementParser>();
        }

        if (opts.Upbit.Enabled)
        {
            services.AddHttpClient(UpbitAnnouncementCollector.HttpClientName, ConfigureJsonClient);
            services.AddSingleton<IAnnouncementCollector, UpbitAnnouncementCollector>();
            services.AddSingleton<IAnnouncementParser, UpbitAnnouncementParser>();
        }

        if (opts.Bithumb.Enabled)
        {
            services.AddHttpClient(BithumbAnnouncementCollector.HttpClientName, ConfigureJsonClient);
            services.AddSingleton<IAnnouncementCollector, BithumbAnnouncementCollector>();
            services.AddSingleton<IAnnouncementParser, BithumbAnnouncementParser>();
        }

        services.AddHostedService<AnnouncementPollingService>();
        return services;
    }

    private static void ConfigureBrowserClient(HttpClient client)
    {
        client.Timeout = TimeSpan.FromSeconds(20);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserUserAgent.Chrome120);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Add("lang", "en");
        client.DefaultRequestHeaders.Add("clienttype", "web");
    }

    private static void ConfigureJsonClient(HttpClient client)
    {
        client.Timeout = TimeSpan.FromSeconds(20);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserUserAgent.Chrome120);
    }
}
