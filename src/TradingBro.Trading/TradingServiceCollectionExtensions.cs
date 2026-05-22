using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingBro.Core.Abstractions;
using TradingBro.Trading.Binance;
using TradingBro.Trading.Common;
using TradingBro.Trading.KillSwitch;

namespace TradingBro.Trading;

public static class TradingServiceCollectionExtensions
{
    public static IServiceCollection AddTradingBroTrading(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TradingOptions>(configuration.GetSection(TradingOptions.SectionName));

        var opts = configuration.GetSection(TradingOptions.SectionName).Get<TradingOptions>() ?? new();

        services.AddSingleton<KillSwitch.KillSwitch>();
        services.AddSingleton<IKillSwitch>(sp => sp.GetRequiredService<KillSwitch.KillSwitch>());

        if (opts.Mode == TradingMode.Paper)
        {
            // Paper mode: PaperTraderService from Strategies project handles signals.
            return services;
        }

        // Real-trading branch — Testnet or Live. Register at least one IExchangeClient.
        if (!string.IsNullOrEmpty(opts.Binance.ApiKey))
        {
            services.AddSingleton<IExchangeClient>(sp =>
                new BinanceExchangeClient(
                    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TradingOptions>>().Value,
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BinanceExchangeClient>>()));
        }

        services.AddHostedService<RealTraderService>();
        return services;
    }
}
