using System.Text;
using ArcaneLibs.Extensions;
using LibMatrix;
using LibMatrix.EventTypes.Spec;
using LibMatrix.Homeservers;
using LibMatrix.RoomTypes;
using LibMatrix.Services;
using MatrixPreviewBot.Extensions;
using OpenGraphNet;
using OpenGraphNet.Metadata;

namespace MatrixPreviewBot;

public class PreviewBot(AuthenticatedHomeserverGeneric hs, ILogger<PreviewBot> logger, HomeserverProviderService hsProviderService, BotConfiguration configuration)
    : IHostedService
{
    private AuthenticatedHomeserverGeneric DecryptedHomeserver => _decryptedHs ?? hs;
    private AuthenticatedHomeserverGeneric? _decryptedHs;

    private static readonly string UserAgent =
        "Mozilla/5.0 (compatible; MatrixPreviewBot; +https://github.com/Enovale/MatrixPreviewBot; embed bot; like Discordbot)";

    private static readonly HttpClient HttpClient = new();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (configuration.DecryptedHomeserverUrl != null)
            _decryptedHs = await hsProviderService.GetAuthenticatedWithToken(configuration.DecryptedHomeserverUrl,
                DecryptedHomeserver.AccessToken,
                DecryptedHomeserver.Proxy);

        HttpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        LinkListener.NewUriSent += UrisReceived;
        await Run(cancellationToken);
        logger.LogInformation("Bot started! " + hs.WhoAmI.UserId);
    }

    public async Task Run(CancellationToken cancellationToken)
    {
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
        var graph = await OpenGraph.ParseUrlAsync(uri, userAgent: UserAgent);

        if (graph.Metadata.Count <= 0)
            return false;

        var previews = new List<ProcessedPreview>();
        var images = graph.Metadata.TryGetValue("og:image", out var value) ? value.ToList() : [];
        var videos = graph.Metadata.TryGetValue("og:video", out var value2) ? value2 : [];
        var audios = graph.Metadata.TryGetValue("og:audio", out var value3) ? value3 : [];

        if (images.Count + videos.Count + audios.Count <= 0)
            return false;

        var tcs = new TaskCompletionSource<bool>();
        _ = ShowProcessingMessage(tcs, graph.Url, room);

        var videoIndex = 0;
        List<StructuredMetadata> toSkip = [];
        // Preload all preview data and assemble the relevant info
        foreach (var media in videos.Concat(images).Concat(audios))
        {
            if (toSkip.Contains(media))
                continue;

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
            var newStream = await (await HttpClient.GetStreamAsync(media.Value)).ToMemoryStreamAsync();
            logger.LogInformation("Downloading {MediaType}: {MediaValue}", preview.MediaContentType, media.Value);
            preview.MediaUrl = await hs.UploadFile(preview.MediaFileName, newStream, preview.MediaContentType);
            preview.MediaSize = newStream.Length;
            if (media.Name == "video")
            {
                var thumbnail = images.Count >= videoIndex + 1 ? images[videoIndex] : null;
                videoIndex++;

                if (thumbnail != null)
                {
                    preview.ThumbnailFileName = GetFileNameFromUrl(thumbnail.Value);
                    preview.ThumbnailContentType = thumbnail.Properties.ValueOrNull("type")?.First() ??
                                                   MimeTypes.GetMimeType(preview.ThumbnailFileName);
                    logger.LogInformation("Downloading {ThumbnailType} for video: {ThumbnailValue}",
                        preview.ThumbnailContentType, thumbnail.Value);
                    var thumbnailStream =
                        await (await HttpClient.GetStreamAsync(thumbnail.Value)).ToMemoryStreamAsync();
                    preview.ThumbnailUrl = await hs.UploadFile(
                        preview.ThumbnailFileName,
                        thumbnailStream,
                        preview.ThumbnailContentType);
                    preview.ThumbnailWidth = int.TryParse(thumbnail.Properties.ValueOrNull("width")?.First().Value,
                        out var twidth)
                        ? twidth
                        : null;
                    preview.ThumbnailHeight = int.TryParse(thumbnail.Properties.ValueOrNull("height")?.First().Value,
                        out var theight)
                        ? theight
                        : null;
                    preview.ThumbnailSize = thumbnailStream.Length;
                    toSkip.Add(thumbnail);
                }
            }

            previews.Add(preview);
        }

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
                    $"<blockquote><div class=\"m13253-url-preview-headline\"><a class=\"m13253-url-preview-backref\" href=\"{graph.Url}\">{new Rune(0x1f517)}{new Rune(0xfe0f)}</a> <strong><a class=\"m13253-url-preview-title\" href=\"{graph.Url}\">{graph.Title}</a></strong>");
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
        var @event = await room.SendMessageEventAsync(new RoomMessageEventContent
        {
            Format = "org.matrix.custom.html",
            FormattedBody =
                $"<blockquote><div class=\"m13253-url-preview-headline\"><a class=\"m13253-url-preview-backref\" href=\"{uri}\">{new Rune(0x23f3)}{new Rune(0xfe0f)} <span class=\"m13253-url-preview-loading\"><em>Loading…</em></span></a></div></blockquote>"
        });
        await decryptedRoom.SendTypingNotificationAsync(true, 10000);
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