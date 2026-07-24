using System.Net;

namespace NexusLabs.Eve;

internal static class EveUrlBuilder
{
    internal static Uri Create(
        string host,
        string routePath,
        IReadOnlyDictionary<string, string>? routeQuery = null)
    {
        string normalizedRoute = routePath.StartsWith("/", StringComparison.Ordinal)
            ? routePath
            : $"/{routePath}";

        if (IsAbsoluteUrl(host)
            && Uri.TryCreate(host, UriKind.Absolute, out Uri? absoluteHost))
        {
            UriBuilder builder = new(absoluteHost)
            {
                Path = $"{TrimTrailingSlash(absoluteHost.AbsolutePath)}{normalizedRoute}",
                Fragment = string.Empty,
            };
            builder.Query = FormatQuery(MergeQuery(ParseQuery(absoluteHost.Query), routeQuery));
            return builder.Uri;
        }

        string withoutFragment = host.Split('#', 2)[0];
        string[] parts = withoutFragment.Split('?', 2);
        string basePath = TrimTrailingSlash(parts[0]);
        List<KeyValuePair<string, string>> query = ParseQuery(
            parts.Length == 2 ? parts[1] : string.Empty);
        string formattedQuery = FormatQuery(MergeQuery(query, routeQuery));
        return new Uri($"{basePath}{normalizedRoute}{PrefixQuery(formattedQuery)}", UriKind.Relative);
    }

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
