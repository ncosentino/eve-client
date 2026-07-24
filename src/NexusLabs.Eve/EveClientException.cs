using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;

namespace NexusLabs.Eve;

/// <summary>
/// Represents a non-successful HTTP response returned by an eve route.
/// </summary>
[SuppressMessage(
    "Roslynator",
    "RCS1194",
    Justification = "Binary exception serialization is obsolete in modern .NET.")]
public sealed class EveClientException : HttpRequestException
{
    /// <summary>
    /// Initializes an exception without an HTTP response.
    /// </summary>
    public EveClientException()
        : this("An eve client request failed.")
    {
    }

    /// <summary>
    /// Initializes an exception with a message but without an HTTP response.
    /// </summary>
    /// <param name="message">The error message.</param>
    public EveClientException(string message)
        : base(message)
    {
        ResponseBody = string.Empty;
        ResponseHeaders = ReadOnlyDictionary<string, IReadOnlyList<string>>.Empty;
    }

    /// <summary>
    /// Initializes an exception with a message and inner exception but without an HTTP response.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying exception.</param>
    public EveClientException(string message, Exception innerException)
        : base(message, innerException)
    {
        ResponseBody = string.Empty;
        ResponseHeaders = ReadOnlyDictionary<string, IReadOnlyList<string>>.Empty;
    }

    internal EveClientException(
        HttpStatusCode statusCode,
        string responseBody,
        IReadOnlyDictionary<string, IReadOnlyList<string>> responseHeaders)
        : base(CreateMessage(statusCode, responseBody), null, statusCode)
    {
        ResponseBody = responseBody;
        ResponseHeaders = responseHeaders;
    }

    /// <summary>
    /// Gets the raw response body.
    /// </summary>
    public string ResponseBody { get; }

    /// <summary>
    /// Gets the response headers keyed case-insensitively.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ResponseHeaders { get; }

    private static string CreateMessage(HttpStatusCode statusCode, string responseBody)
    {
        if (responseBody.Length == 0)
        {
            return $"The eve server returned HTTP {(int)statusCode}.";
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out JsonElement error)
                && error.ValueKind == JsonValueKind.String)
            {
                return error.GetString() ?? responseBody;
            }
        }
        catch (JsonException)
        {
            return responseBody;
        }

        return responseBody;
    }
}
