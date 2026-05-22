using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using TradingBro.Core.Abstractions;
using TradingBro.Notifications.Telegram;

namespace TradingBro.Notifications;

public static class NotificationsServiceCollectionExtensions
{
    public static IServiceCollection AddTradingBroNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TelegramOptions>(configuration.GetSection(TelegramOptions.SectionName));

        var opts = configuration.GetSection(TelegramOptions.SectionName).Get<TelegramOptions>() ?? new TelegramOptions();
        var telegramReady = opts.Enabled && !string.IsNullOrWhiteSpace(opts.BotToken);

        services.AddSingleton<MuteState>();

        if (telegramReady)
        {
            services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(opts.BotToken));

            // Concrete TelegramNotifier owns the in-memory queue; expose it as
            // both itself (so TelegramSendingService can read it) and as INotifier.
            services.AddSingleton<TelegramNotifier>();
            services.AddSingleton<INotifier>(sp => sp.GetRequiredService<TelegramNotifier>());

            services.AddHostedService<TelegramSendingService>();
            services.AddHostedService<AnnouncementSubscriber>();

            if (opts.CommandsEnabled)
            {
                services.AddHostedService<TelegramCommandService>();
            }
        }
        else
        {
            services.AddSingleton<INotifier, NoopNotifier>();
        }

        return services;
    }
}
