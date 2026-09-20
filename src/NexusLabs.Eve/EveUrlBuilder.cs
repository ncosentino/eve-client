using System.Net;

namespace NexusLabs.Eve;

internal static class EveUrlBuilder
{
    private const string EveNamedAgentMountPrefix = "/eve/";
    private const string EveRoutePrefix = "/eve/v1";

    internal static Uri Create(
        string host,
        string routePath,
        IReadOnlyDictionary<string, string>? routeQuery = null)
    {
        string[] routeParts = routePath.Split('?', 2);
        string routeWithoutQuery = routeParts[0];
        string embeddedQuery = routeParts.Length == 2 ? routeParts[1] : string.Empty;
        string normalizedRoute = routeWithoutQuery.StartsWith("/", StringComparison.Ordinal)
            ? routeWithoutQuery
            : $"/{routeWithoutQuery}";

        if (IsAbsoluteUrl(host)
            && Uri.TryCreate(host, UriKind.Absolute, out Uri? absoluteHost))
        {
            UriBuilder builder = new(absoluteHost)
            {
                Path = JoinRoutePath(
                    TrimTrailingSlash(absoluteHost.AbsolutePath),
                    normalizedRoute),
                Fragment = string.Empty,
            };
            List<KeyValuePair<string, string>> absoluteQuery = ParseQuery(absoluteHost.Query);
            absoluteQuery.AddRange(ParseQuery(embeddedQuery));
            builder.Query = FormatQuery(MergeQuery(absoluteQuery, routeQuery));
            return builder.Uri;
        }

        string withoutFragment = host.Split('#', 2)[0];
        string[] parts = withoutFragment.Split('?', 2);
        string basePath = TrimTrailingSlash(parts[0]);
        List<KeyValuePair<string, string>> query = ParseQuery(
            parts.Length == 2 ? parts[1] : string.Empty);
        query.AddRange(ParseQuery(embeddedQuery));
        string formattedQuery = FormatQuery(MergeQuery(query, routeQuery));
        return new Uri(
            $"{JoinRoutePath(basePath, normalizedRoute)}{PrefixQuery(formattedQuery)}",
            UriKind.Relative);
    }

    private static string JoinRoutePath(string basePath, string routePath)
    {
        if (!IsEveProtocolRoute(routePath))
        {
            return $"{basePath}{routePath}";
        }

        if (IsCompactNamedAgentMount(basePath))
        {
            return $"{basePath}{routePath["/eve".Length..]}";
        }

        if (IsCompactNamedAgentProtocolMount(basePath))
        {
            return $"{basePath}{routePath[EveRoutePrefix.Length..]}";
        }

        return $"{basePath}{routePath}";
    }

    private static bool IsEveProtocolRoute(string path) =>
        string.Equals(path, EveRoutePrefix, StringComparison.Ordinal)
        || path.StartsWith($"{EveRoutePrefix}/", StringComparison.Ordinal);

    private static bool IsCompactNamedAgentMount(string path) =>
        path.StartsWith(EveNamedAgentMountPrefix, StringComparison.Ordinal)
        && IsValidAgentName(path.AsSpan(EveNamedAgentMountPrefix.Length));

    private static bool IsCompactNamedAgentProtocolMount(string path)
    {
        const string protocolSuffix = "/v1";
        return path.StartsWith(EveNamedAgentMountPrefix, StringComparison.Ordinal)
            && path.EndsWith(protocolSuffix, StringComparison.Ordinal)
            && IsValidAgentName(path.AsSpan(
                EveNamedAgentMountPrefix.Length,
                path.Length - EveNamedAgentMountPrefix.Length - protocolSuffix.Length));
    }

    private static bool IsValidAgentName(ReadOnlySpan<char> name)
    {
        if (name.IsEmpty || !IsAsciiLowerLetterOrDigit(name[0]))
        {
            return false;
        }

        for (int index = 1; index < name.Length; index++)
        {
            char character = name[index];
            if (!IsAsciiLowerLetterOrDigit(character)
                && character is not '_' and not '-')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLowerLetterOrDigit(char value) =>
        value is >= 'a' and <= 'z' or >= '0' and <= '9';

    private static string TrimTrailingSlash(string value) =>
        value == "/"
            ? string.Empty
            : value.EndsWith("/", StringComparison.Ordinal)
                ? value[..^1]
                : value;

    private static bool IsAbsoluteUrl(string value)
    {
        if (value.Length == 0 || !char.IsAsciiLetter(value[0]))
        {
            return false;
        }

        for (int index = 1; index < value.Length; index++)
        {
            char character = value[index];
            if (character == ':')
            {
                return true;
            }

            if (!char.IsAsciiLetterOrDigit(character)
                && character is not '+' and not '-' and not '.')
            {
                return false;
            }
        }

        return false;
    }

    private static List<KeyValuePair<string, string>> ParseQuery(string query)
    {
        string normalized = query.TrimStart('?');
        List<KeyValuePair<string, string>> values = [];
        if (normalized.Length == 0)
        {
            return values;
        }

        foreach (string pair in normalized.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            string name = WebUtility.UrlDecode(parts[0]);
            string value = parts.Length == 2 ? WebUtility.UrlDecode(parts[1]) : string.Empty;
            values.Add(new KeyValuePair<string, string>(name, value));
        }

        return values;
    }

    private static List<KeyValuePair<string, string>> MergeQuery(
        List<KeyValuePair<string, string>> values,
        IReadOnlyDictionary<string, string>? overrides)
    {
        if (overrides is null)
        {
            return values;
        }

        foreach ((string name, string value) in overrides)
        {
            int firstIndex = values.FindIndex(pair =>
                string.Equals(pair.Key, name, StringComparison.Ordinal));
            values.RemoveAll(pair => string.Equals(pair.Key, name, StringComparison.Ordinal));
            int insertIndex = firstIndex < 0 ? values.Count : Math.Min(firstIndex, values.Count);
            values.Insert(insertIndex, new KeyValuePair<string, string>(name, value));
        }

        return values;
    }

    private static string FormatQuery(IEnumerable<KeyValuePair<string, string>> values) =>
        string.Join(
            "&",
            values.Select(static pair =>
                $"{WebUtility.UrlEncode(pair.Key)}={WebUtility.UrlEncode(pair.Value)}"));

    private static string PrefixQuery(string query) => query.Length == 0 ? string.Empty : $"?{query}";
}
