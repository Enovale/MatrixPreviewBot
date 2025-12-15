using System.Text;
using ArcaneLibs.Extensions;
using LibMatrix;
using LibMatrix.EventTypes.Spec;
using LibMatrix.Homeservers;
using LibMatrix.RoomTypes;
using LibMatrix.Services;
using LibMatrix.Utilities.Bot;
using MatrixPreviewBot.Configuration;
using MatrixPreviewBot.Extensions;
using Microsoft.Extensions.Caching.Memory;
using OpenGraphNet;
using OpenGraphNet.Metadata;

namespace MatrixPreviewBot;

public class PreviewBot(
    AuthenticatedHomeserverGeneric hs,
    ILogger<PreviewBot> logger,
    HomeserverProviderService hsProviderService,
    BotConfiguration configuration,
    IMemoryCache memCache)
    : IHostedService
{
    private AuthenticatedHomeserverGeneric DecryptedHomeserver => _decryptedHs ?? hs;
    private AuthenticatedHomeserverGeneric? _decryptedHs;

    private static readonly HttpClient HttpClient = new();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (configuration.DecryptedHomeserverUrl != null)
            _decryptedHs = await hsProviderService.GetAuthenticatedWithToken(configuration.DecryptedHomeserverUrl,
                DecryptedHomeserver.AccessToken,
                DecryptedHomeserver.Proxy);

        HttpClient.DefaultRequestHeaders.Add("User-Agent", configuration.UserAgent);
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
            var anythingReturned = false;

            foreach (var uri in uris)
            {
                if (await ProcessUri(room, uri, shouldDelete ? $"{@event.Sender} sent:" : null))
                    anythingReturned = true;
            }

            if (anythingReturned && shouldDelete)
                _ = DecryptedHomeserver.GetRoom(room.RoomId).RedactEventAsync(@event.EventId!, "URL Preview provided.");
        }
        catch (Exception e)
        {
            throw; // TODO handle exception
        }
    }

    private async Task<bool> ProcessUri(GenericRoom room, Uri uri, string? prefix = null)
    {
        OpenGraph graph;
        try
        {
            graph = await OpenGraph.ParseUrlAsync(uri, userAgent: configuration.UserAgent);
        }
        catch (Exception e)
        {
            logger.LogCritical("{Exception}", e);
            return false;
        }

        if (graph.Metadata.Count <= 0)
            return false;

        var previews = new List<ProcessedPreview>();
        var images = graph.Metadata.TryGetValue("og:image", out var value) ? value.ToList() : [];
        var videos = graph.Metadata.TryGetValue("og:video", out var value2) ? value2.ToList() : [];
        var audios = graph.Metadata.TryGetValue("og:audio", out var value3) ? value3 : [];

        if (images.Count + videos.Count + audios.Count <= 0)
            return false;

        var tcs = new TaskCompletionSource<bool>();
        _ = ShowProcessingMessage(tcs, graph.OriginalUrl, room);

        var videoIndex = 0;
        List<Task> tasks = [];
        // Preload all preview data and assemble the relevant info
        foreach (var media in videos.Concat(images.Skip(videos.Count)).Concat(audios))
        {
            if (memCache.TryGetValue(media.Value, out var cachedPreview) && cachedPreview is ProcessedPreview cp)
            {
                logger.LogInformation("{MediaValue}: Cache hit!", media.Value);
                previews.Add(cp);
                continue;
            }

            logger.LogInformation("{MediaValue}: Cache miss!", media.Value);

            var preview = new ProcessedPreview
            {
                PreviewType = media.Name,
                MediaFileName = GetFileNameFromUrl(media.Value)
            };

            preview.MediaContentType = media.Properties.ValueOrNull("type")?.First() ??
                                       MimeTypes.GetMimeType(preview.MediaFileName);
            preview.MediaWidth = int.TryParse(media.Properties.ValueOrNull("width")?.First().Value, out var width)
                ? width
                : null;
            preview.MediaHeight = int.TryParse(media.Properties.ValueOrNull("height")?.First().Value, out var height)
                ? height
                : null;

            tasks.Add(DownloadMedia());
            if (media.Name == "video")
            {
                var thumbnail = images.Count >= videoIndex + 1 ? images[videoIndex] : null;
                videoIndex++;

                if (thumbnail != null)
                {
                    preview.ThumbnailFileName = GetFileNameFromUrl(thumbnail.Value);
                    preview.ThumbnailContentType = thumbnail.Properties.ValueOrNull("type")?.First() ??
                                                   MimeTypes.GetMimeType(preview.ThumbnailFileName);

                    tasks.Add(DownloadThumbnail());

                    preview.ThumbnailWidth = int.TryParse(thumbnail.Properties.ValueOrNull("width")?.First().Value,
                        out var twidth)
                        ? twidth
                        : null;
                    preview.ThumbnailHeight = int.TryParse(thumbnail.Properties.ValueOrNull("height")?.First().Value,
                        out var theight)
                        ? theight
                        : null;

                    async Task DownloadThumbnail()
                    {
                        logger.LogInformation("Downloading {ThumbnailType} for video: {ThumbnailValue}",
                            preview.ThumbnailContentType, thumbnail.Value);
                        using var thumbnailStream =
                            await (await HttpClient.GetStreamAsync(thumbnail.Value)).ToMemoryStreamAsync();
                        preview.ThumbnailUrl = await hs.UploadFile(
                            preview.ThumbnailFileName,
                            thumbnailStream,
                            preview.ThumbnailContentType);
                        preview.ThumbnailSize = thumbnailStream.Length;
                        memCache.Set(media.Value, preview);
                    }
                }
            }

            previews.Add(preview);
            continue;

            async Task DownloadMedia()
            {
                logger.LogInformation("Downloading {MediaType}: {MediaValue}", preview.MediaContentType,
                    media.Value);
                using var newStream = await (await HttpClient.GetStreamAsync(media.Value)).ToMemoryStreamAsync();
                preview.MediaUrl = await hs.UploadFile(preview.MediaFileName, newStream, preview.MediaContentType);
                preview.MediaSize = newStream.Length;
                memCache.Set(media.Value, preview);
            }
        }

        Task.WaitAll(tasks.ToArray());

        // Send the loaded previews
        for (var i = 0; i < previews.Count; i++)
        {
            var preview = previews[i];
            var content = new RoomMessageEventContent
            {
                MessageType = "m." + preview.PreviewType,
                Url = preview.MediaUrl,
                FileName = preview.MediaFileName,
                FileInfo = new RoomMessageEventContent.FileInfoStruct
                {
                    MimeType = preview.MediaContentType,
                    Width = preview.MediaWidth,
                    Height = preview.MediaHeight,
                    Size = preview.MediaSize,
                    ThumbnailUrl = preview.ThumbnailUrl,
                    ThumbnailInfo = new RoomMessageEventContent.FileInfoStruct.ThumbnailInfoStruct
                    {
                        MimeType = preview.ThumbnailContentType,
                        Width = preview.ThumbnailWidth,
                        Height = preview.ThumbnailHeight,
                        Size = preview.ThumbnailSize
                    },
                }
            };

            if (prefix != null && i == 0)
            {
                _ = room.SendMessageEventAsync(new RoomMessageEventContent(body: prefix));
            }

            // Create the body if it's the last preview
            if (i == previews.Count - 1)
            {
                await using var writer = new StringWriter();
                await writer.WriteLineAsync($"> {graph.Title}");
                var description = graph.Metadata.GetOrNull("og:description")?.FirstOrDefault()?.Value;
                await writer.WriteLineAsync($"> {description}");
                content.Body = writer.ToString();
                await using var html = new StringWriter();
                await html.WriteAsync(
                    $"<blockquote><div class=\"m13253-url-preview-headline\"><a class=\"m13253-url-preview-backref\" href=\"{graph.OriginalUrl}\">{new Rune(0x1f517)}{new Rune(0xfe0f)}</a> <strong><a class=\"m13253-url-preview-title\" href=\"{graph.OriginalUrl}\">{graph.Title}</a></strong>");
                await html.WriteAsync($"<div class=\"m13253-url-preview-description\">{description}</div>");
                content.FormattedBody = html.ToString();
                content.Format = "org.matrix.custom.html";
            }

            _ = room.SendMessageEventAsync(content);
        }

        // Tell processing routine that we're done
        tcs.SetResult(true);
        return true;
    }

    private async Task ShowProcessingMessage(TaskCompletionSource<bool> tcs, Uri? uri, GenericRoom room)
    {
        var decryptedRoom = DecryptedHomeserver.GetRoom(room.RoomId);
        await decryptedRoom.SendTypingNotificationAsync(true);
        var @event = await room.SendMessageEventAsync(new RoomMessageEventContent
        {
            Format = "org.matrix.custom.html",
            FormattedBody =
                $"<blockquote><div class=\"m13253-url-preview-headline\"><a class=\"m13253-url-preview-backref\" href=\"{uri}\">{new Rune(0x23f3)}{new Rune(0xfe0f)} <span class=\"m13253-url-preview-loading\"><em>Loading…</em></span></a></div></blockquote>"
        });

        // Wait for previewing to complete
        await tcs.Task;
        await decryptedRoom.RedactEventAsync(@event.EventId, "Temporary, embed has been provided.");
        await decryptedRoom.SendTypingNotificationAsync(false);
    }

    private static string GetFileNameFromUrl(string url)
    {
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