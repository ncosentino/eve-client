using System.Net;
using System.Text;

namespace NexusLabs.Eve.Tests;

internal static class EveTestResponses
{
    internal static HttpResponseMessage StreamResponse(
        string body,
        int? tailIndex = null,
        string streamVersion = EveProtocol.MessageStreamVersion)
    {
        HttpResponseMessage response = new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, EveProtocol.MessageStreamContentType),
        };
        response.Headers.TryAddWithoutValidation(EveProtocol.StreamVersionHeaderName, streamVersion);
        if (tailIndex is int value)
        {
            response.Headers.TryAddWithoutValidation(
                "x-eve-stream-tail-index",
                value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return response;
    }
}
