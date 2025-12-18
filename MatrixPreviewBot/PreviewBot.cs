using System.Text;
using ArcaneLibs.Extensions;
using LibMatrix;
using LibMatrix.EventTypes.Spec;
using LibMatrix.Homeservers;
using LibMatrix.RoomTypes;
using LibMatrix.Services;
using MatrixPreviewBot.Configuration;
using MatrixPreviewBot.Extensions;
using MatrixPreviewBot.Processors;
using Microsoft.Extensions.Caching.Memory;
using OpenGraphNet;

namespace MatrixPreviewBot;

public class PreviewBot(
    AuthenticatedHomeserverGeneric hs,
    ILogger<PreviewBot> logger,
    HomeserverProviderService hsProviderService,
    BotConfiguration configuration,
    IMemoryCache memCache,
    HttpClient httpClient,
    TumblrProcessor tumblrProcessor,
    DirectMediaProcessor directMediaProcessor,
    OpenGraphProcessor openGraphProcessor)
    : IHostedService
{
    private AuthenticatedHomeserverGeneric DecryptedHomeserver => _decryptedHs ?? hs;
    private AuthenticatedHomeserverGeneric? _decryptedHs;

    private List<ProcessorBase> Processors = [tumblrProcessor, directMediaProcessor, openGraphProcessor];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (configuration.DecryptedHomeserverUrl != null)
            _decryptedHs = await hsProviderService.GetAuthenticatedWithToken(configuration.DecryptedHomeserverUrl,
                DecryptedHomeserver.AccessToken,
                DecryptedHomeserver.Proxy);

        httpClient.DefaultRequestHeaders.Add("User-Agent", configuration.UserAgent);
        LinkListener.NewUriSent += UrisReceived;
        await Run(cancellationToken);
        logger.LogInformation("Bot started! " + hs.WhoAmI.UserId);
    }

    public async Task Run(CancellationToken cancellationToken)
    {
#if DEBUG
        foreach (var room in await hs.GetJoinedRooms())
        {
            try
            {
                _ = room.SendMessageEventAsync(new RoomMessageEventContent(body: "Ready!"));
            }
            catch (Exception e)
            {
                // Stub
            }
        }
#endif
    }

    private async void UrisReceived(MatrixEventResponse @event, List<Uri> uris, bool containsOtherText)
    {
        try
        {
            var room = hs.GetRoom(@event.RoomId!);
            var shouldDelete = configuration.DeleteOriginalIfEmpty && !containsOtherText;
            var prefix = shouldDelete ? $"{@event.Sender} sent:" : null;
            var anythingReturned = false;
            List<IEnumerable<RoomMessageEventContent>> results = [];

            foreach (var uri in uris)
            {
                var tcs = new TaskCompletionSource<bool>();
                _ = ShowProcessingMessage(tcs, uri, room);

                foreach (var processor in Processors)
                {
                    var result = await processor.ProcessUriAsync(room, uri);
                    if (result != null)
                        results.Add(result);
                }

                tcs.SetResult(true);
            }

            foreach (var resultSet in results)
            {
                foreach (var message in resultSet)
                {
                    if (!anythingReturned)
                        _ = room.SendMessageEventAsync(new RoomMessageEventContent(body: prefix));

                    anythingReturned = true;

                    _ = room.SendMessageEventAsync(message);
                }
            }

            if (anythingReturned && shouldDelete)
                _ = DecryptedHomeserver.GetRoom(room.RoomId).RedactEventAsync(@event.EventId!, "URL Preview provided.");
        }
        catch (Exception e)
        {
            logger.LogCritical("{Exception}", e);
        }
    }

    private async Task ShowProcessingMessage(TaskCompletionSource<bool> tcs, Uri? uri, GenericRoom room)
    {
        var decryptedRoom = DecryptedHomeserver.GetRoom(room.RoomId);
        _ = decryptedRoom.SendTypingNotificationAsync(true);
        var @event = await room.SendMessageEventAsync(new RoomMessageEventContent
        {
            Format = "org.matrix.custom.html",
            FormattedBody =
                $"<blockquote><div class=\"m13253-url-preview-headline\"><a class=\"m13253-url-preview-backref\" href=\"{uri}\">{new Rune(0x23f3)}{new Rune(0xfe0f)} <span class=\"m13253-url-preview-loading\"><em>Loading…</em></span></a></div></blockquote>"
        });

        // Wait for previewing to complete
        while (!tcs.Task.IsCompleted)
        {
            await Task.Delay(1000);
            await decryptedRoom.SendTypingNotificationAsync(true);
        }

        await decryptedRoom.RedactEventAsync(@event.EventId, "Temporary, embed has been provided.");
        await decryptedRoom.SendTypingNotificationAsync(false);
    }

    internal static string? GetFileNameFromUrl(string? url)
    {
        if (url is null)
            return null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            uri = new Uri(url);

        return Path.GetFileName(uri.LocalPath);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Bot shutting down!");
        LinkListener.NewUriSent -= UrisReceived;
    }
}