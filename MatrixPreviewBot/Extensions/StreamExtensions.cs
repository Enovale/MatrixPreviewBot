namespace MatrixPreviewBot.Extensions;

public static class StreamExtensions
{
    public static async Task<MemoryStream> ToMemoryStreamAsync(this Stream stream)
    {
        var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return ms;
    }
}