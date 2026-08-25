using System.Text.Json;

namespace NexusLabs.Eve;

internal static class EveHealthResponseParser
{
    private const int MaximumIssueCount = 5;
    private static readonly string[] AllowedProperties = ["ok", "status", "workflowId"];

    public static EveHealthStatus Parse(string body)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            throw new EveHealthResponseException(
                "The server returned an unrecognized eve health response.",
                exception);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            List<EveHealthValidationIssue> issues = [];
            if (root.ValueKind != JsonValueKind.Object)
            {
                AddIssue(issues, string.Empty, "Expected an object.");
                throw new EveHealthResponseException(issues);
            }

            ValidateBooleanLiteral(root, "ok", true, issues);
            ValidateStringLiteral(root, "status", "ready", issues);
            ValidateWorkflowId(root, issues);
            ValidateUnknownProperties(root, issues);

            if (issues.Count > 0)
            {
                throw new EveHealthResponseException(issues);
            }

            return new EveHealthStatus(
                true,
                "ready",
                root.GetProperty("workflowId").GetString()!);
        }
    }

    private static void ValidateBooleanLiteral(
        JsonElement root,
        string propertyName,
        bool expected,
        List<EveHealthValidationIssue> issues)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            AddIssue(issues, propertyName, "Required.");
            return;
        }

        if (value.ValueKind is not JsonValueKind.True and not JsonValueKind.False
            || value.GetBoolean() != expected)
        {
            AddIssue(issues, propertyName, $"Expected literal {expected.ToString().ToLowerInvariant()}.");
        }
    }

    private static void ValidateStringLiteral(
        JsonElement root,
        string propertyName,
        string expected,
        List<EveHealthValidationIssue> issues)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            AddIssue(issues, propertyName, "Required.");
            return;
        }

        if (value.ValueKind != JsonValueKind.String
            || !string.Equals(value.GetString(), expected, StringComparison.Ordinal))
        {
            AddIssue(issues, propertyName, $"Expected literal \"{expected}\".");
        }
    }

    private static void ValidateWorkflowId(
        JsonElement root,
        List<EveHealthValidationIssue> issues)
    {
        const string propertyName = "workflowId";
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            AddIssue(issues, propertyName, "Required.");
            return;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            AddIssue(issues, propertyName, "Expected a string.");
            return;
        }

        if (value.GetString()!.Length == 0)
        {
            AddIssue(issues, propertyName, "Expected at least one character.");
        }
    }

    private static void ValidateUnknownProperties(
        JsonElement root,
        List<EveHealthValidationIssue> issues)
    {
        List<string> unknownProperties = [];
        using JsonElement.ObjectEnumerator properties = root.EnumerateObject();
        while (properties.MoveNext())
        {
            string propertyName = properties.Current.Name;
            if (!AllowedProperties.Contains(propertyName, StringComparer.Ordinal))
            {
                unknownProperties.Add(propertyName);
            }
        }

        if (unknownProperties.Count > 0)
        {
            AddIssue(
                issues,
                string.Empty,
                $"Unrecognized properties: {string.Join(", ", unknownProperties)}.");
        }
    }

    private static void AddIssue(
        List<EveHealthValidationIssue> issues,
        string path,
        string message)
    {
        if (issues.Count < MaximumIssueCount)
        {
            issues.Add(new EveHealthValidationIssue(path, message));
        }
    }
}
