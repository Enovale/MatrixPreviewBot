using System.Reflection;
using LibMatrix;
using LibMatrix.EventTypes.Spec;
using LibMatrix.Homeservers;
using LibMatrix.RoomTypes;
using MatrixPreviewBot.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenGraphNet;
using OpenGraphNet.Metadata;

namespace MatrixPreviewBot;

public class PreviewBot(AuthenticatedHomeserverGeneric hs, ILogger<PreviewBot> logger, BotConfiguration configuration)
    : IHostedService
{
    private static readonly string UserAgent = "PreviewBot " + Assembly.GetExecutingAssembly().GetName().Version;
    private static readonly HttpClient HttpClient = new();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        HttpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        LinkListener.NewUriSent += UrisReceived;
        await Run(cancellationToken);
        logger.LogInformation("Bot started! " + hs.WhoAmI.UserId);
    }

    public async Task Run(CancellationToken cancellationToken)
    {
        foreach (var room in await hs.GetJoinedRooms())
        {
            await room.SendMessageEventAsync(new RoomMessageEventContent(body: "Ready!"));
            // var fileStream1 = File.OpenRead("/home/enova/Downloads/test.png");
            // var url1 = await hs.UploadFile("test.png", fileStream1, "image/png");
            // var fileStream2 = File.OpenRead("/home/enova/Pictures/wallpaper.jpg");
            // var url2 = await hs.UploadFile("test2.jpg", fileStream2, "image/jpeg");
            // _ = room.SendMessageEventAsync(new RoomMessageEventContent {
            //     MessageType = "m.image",
            //     Url = url1,
            //     Body = "1",
            //     FileName = "test.png",
            //     FileInfo = new RoomMessageEventContent.FileInfoStruct {
            //         Size = fileStream1.Length,
            //         MimeType = "image/png"
            //     }
            // });
            // _ = room.SendMessageEventAsync(new RoomMessageEventContent {
            //     MessageType = "m.image",
            //     Url = url2,
            //     Body = "2",
            //     FileName = "test2.jpg",
            //     FileInfo = new RoomMessageEventContent.FileInfoStruct {
            //         Size = fileStream2.Length,
            //         MimeType = "image/jpeg"
            //     }
            // });
            // await room.SendMessageEventAsync(new RoomMessageEventContent
            // {
            //     FormattedBody = $"<img src=\"{url1}\"> <img src=\"{url2}\">",
            //     Format = "org.matrix.custom.html",
            //     MessageType = "m.text"
            // });
        }
    }

    private async void UrisReceived(MatrixEventResponse @event, List<Uri> uris, bool containsOtherText)
    {
        try
        {
            var room = hs.GetRoom(@event.RoomId!);

            foreach (var uri in uris)
            {
                await ProcessUri(room, uri);
            }

            if (!containsOtherText)
                // TODO: Doesn't actually work ??
                _ = room.RedactEventAsync(@event.EventId!, "URL Preview provided.");
        }
        catch (Exception e)
        {
            throw; // TODO handle exception
        }
    }

    private async Task ProcessUri(GenericRoom room, Uri uri)
    {
        var graph = await OpenGraph.ParseUrlAsync(uri, userAgent: UserAgent);

        if (graph.Metadata.Count <= 0)
            return;

        var previews = new List<ProcessedPreview>();
        var images = graph.Metadata.TryGetValue("og:image", out var value) ? value.ToList() : [];
        var videos = graph.Metadata.TryGetValue("og:video", out var value2) ? value2 : [];
        var audios = graph.Metadata.TryGetValue("og:audio", out var value3) ? value3 : [];
        var videoIndex = 0;
        List<StructuredMetadata> toSkip = [];
        foreach (var media in videos)
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
            preview.MediaWidth = int.TryParse(media.Properties.ValueOrNull("width")?.First()?.Value, out var width)
                ? width
                : null;
            preview.MediaHeight = int.TryParse(media.Properties.ValueOrNull("height")?.First()?.Value, out var height)
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
                    logger.LogInformation("Downloading {ThumbnailType} for video: {ThumbnailValue}", preview.ThumbnailContentType, thumbnail.Value);
                    var thumbnailStream = await (await HttpClient.GetStreamAsync(thumbnail.Value)).ToMemoryStreamAsync();
                    preview.ThumbnailUrl = await hs.UploadFile(
                        preview.ThumbnailFileName,
                        thumbnailStream,
                        preview.ThumbnailContentType);
                    preview.ThumbnailWidth = int.TryParse(thumbnail.Properties.ValueOrNull("width")?.First()?.Value, out var twidth)
                        ? twidth
                        : null;
                    preview.ThumbnailHeight = int.TryParse(thumbnail.Properties.ValueOrNull("height")?.First()?.Value, out var theight)
                        ? theight
                        : null;
                    preview.ThumbnailSize = thumbnailStream.Length;
                    toSkip.Add(thumbnail);
                }
            }

            previews.Add(preview);
        }

        for (var i = 0; i < previews.Count; i++)
        {
            var preview = previews[i];
            var content = new RoomMessageEventContent
            {
                MessageType = "m." + preview.PreviewType,
                Url = preview.MediaUrl,
                Body = preview.MediaFileName,
                FileName = preview.MediaFileName,
                FileInfo = new RoomMessageEventContent.FileInfoStruct
                {
                    Duration = 19100,
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

            if (i == previews.Count - 1)
            {
                content.Body = graph.Title;
                content.FormattedBody = content.Body;
                content.Format = "org.matrix.custom.html";
            }

            _ = room.SendMessageEventAsync(content);
        }

        logger.LogCritical(graph.ToString());
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