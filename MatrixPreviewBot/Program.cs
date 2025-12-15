using LibMatrix.Services;
using LibMatrix.Utilities.Bot;
using MatrixPreviewBot;
using MatrixPreviewBot.Configuration;
using MatrixPreviewBot.Handlers;

var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureHostOptions(host => {
    host.ServicesStartConcurrently = true;
    host.ServicesStopConcurrently = true;
    host.ShutdownTimeout = TimeSpan.FromSeconds(5);
});

if (Environment.GetEnvironmentVariable("URL_PREVIEW_BOT_APPSETTINGS_PATH") is { } path)
    builder.ConfigureAppConfiguration(x => x.AddJsonFile(path));

var host = builder.ConfigureServices((_, services) => {
    services.AddSingleton<BotConfiguration>();
    services.AddSingleton<LinkListenerConfiguration>();
    services.AddMemoryCache();

    services.AddRoryLibMatrixServices(new() {
        AppName = "UrlPreviewBot"
    });
    services.AddMatrixBot()
        .WithInviteHandler(InviteHandler.HandleAsync)
        ;

    services.AddHostedService<LinkListener>();
    services.AddHostedService<PreviewBot>();
}).UseConsoleLifetime().Build();

await host.RunAsync();