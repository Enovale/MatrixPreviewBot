using LibMatrix.EventTypes.Spec;
using LibMatrix.RoomTypes;

namespace MatrixPreviewBot.Extensions;

public static class GenericRoomExtensions
{
    public static async Task SendFileWithBodyAsync(this GenericRoom room, string fileName, Stream fileStream, string body, string? formattedBody = null, string messageType = "m.file", string contentType = "application/octet-stream")
    {
        var url = await room.Homeserver.UploadFile(fileName, fileStream, contentType);
        var content = new RoomMessageEventContent {
            MessageType = messageType,
            Url = url,
            Body = body,
            Format = formattedBody != null ? "org.matrix.custom.html" : null,
            FormattedBody = formattedBody ?? body,
            FileName = fileName,
            FileInfo = new RoomMessageEventContent.FileInfoStruct {
                Size = fileStream.Length,
                MimeType = contentType
            }
        };
        await room.SendTimelineEventAsync("m.room.message", content);
    }
}