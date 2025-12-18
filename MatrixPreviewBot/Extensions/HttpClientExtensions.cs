using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace MatrixPreviewBot.Extensions;

public static class HttpClientExtensions
{
    extension(HttpClient httpClient)
    {
        public async Task<MemoryStream> GetPageInMemoryAsync([StringSyntax("Uri"), UriString("GET")] string? requestUri)
        {
            return await (await httpClient.GetStreamAsync(requestUri).ConfigureAwait(false)).ToMemoryStreamAsync()
                .ConfigureAwait(false);
        }

        public async Task<MemoryStream> GetPageInMemoryAsync(Uri? requestUri)
        {
            return await (await httpClient.GetStreamAsync(requestUri).ConfigureAwait(false)).ToMemoryStreamAsync()
                .ConfigureAwait(false);
        }
    }
}