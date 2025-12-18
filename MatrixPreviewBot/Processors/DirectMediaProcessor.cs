using LibMatrix.EventTypes.Spec;
using LibMatrix.Homeservers;
using LibMatrix.RoomTypes;
using MatrixPreviewBot.Extensions;
using Microsoft.Extensions.Caching.Memory;

namespace MatrixPreviewBot.Processors;

public class DirectMediaProcessor(AuthenticatedHomeserverGeneric hs, IMemoryCache memCache, HttpClient httpClient)
    : ProcessorBase
{
    public override async Task<IEnumerable<RoomMessageEventContent>?> ProcessUriAsync(GenericRoom room, Uri uri)
    {
        var head = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, uri));

        var mimeType = head.Content.Headers.ContentType?.MediaType;
        var mimeCategory = mimeType?.Split("/").First();

        if (mimeCategory is not ("image" or "video" or "audio"))
            return null;

        var realUrl = head.RequestMessage?.RequestUri?.ToString();

        if (realUrl is null)
            return null;

        if (!memCache.TryGetValue(realUrl, out var preview) || preview is not ProcessedPreview cp)
        {
            using var stream = await (await httpClient.GetStreamAsync(uri)).ToMemoryStreamAsync();
            var fileName = PreviewBot.GetFileNameFromUrl(realUrl);
            var mediaUrl = await hs.UploadFile(fileName!, stream, mimeType!);
            cp = new ProcessedPreview
            {
                MediaFileName = fileName!,
                MediaSize = stream.Length,
                MediaUrl = mediaUrl
            };
            memCache.Set(realUrl, cp);
        }

        return
        [
            new RoomMessageEventContent
            {
                FileName = cp.MediaFileName,
                Url = cp.MediaUrl,
                MessageType = "m." + mimeCategory,
                FileInfo = new RoomMessageEventContent.FileInfoStruct
                {
                    Size = cp.MediaSize,
                    MimeType = mimeType,
                }
            }
        ];
    }
}