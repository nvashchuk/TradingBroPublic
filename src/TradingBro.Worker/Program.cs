using Microsoft.EntityFrameworkCore;
using Serilog;
using TradingBro.Collectors;
using TradingBro.Data;
using TradingBro.Notifications;
using TradingBro.Strategies;
using TradingBro.Trading;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables(prefix: "TRADINGBRO_");

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Services.AddSerilog();

var sqliteConn = builder.Configuration.GetConnectionString("Sqlite")
    ?? throw new InvalidOperationException("Missing connection string 'Sqlite' in configuration");

var dbPath = sqliteConn.Replace("Data Source=", string.Empty, StringComparison.OrdinalIgnoreCase);
var dbDir = Path.GetDirectoryName(dbPath);
if (!string.IsNullOrEmpty(dbDir))
{
    Directory.CreateDirectory(dbDir);
}

builder.Services.AddDbContext<TradingBroDbContext>(opts => opts.UseSqlite(sqliteConn));

// Register subscribers BEFORE publishers so their hosted services start first
// and have time to subscribe before the polling service starts publishing.
builder.Services.AddTradingBroNotifications(builder.Configuration);
builder.Services.AddTradingBroStrategies(builder.Configuration);
builder.Services.AddTradingBroTrading(builder.Configuration);
builder.Services.AddTradingBroCollectors(builder.Configuration);

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TradingBroDbContext>();
    await db.Database.MigrateAsync();
}

try
{
    await host.RunAsync();
}
finally
{
    await Log.CloseAndFlushAsync();
}

public partial class Program;
