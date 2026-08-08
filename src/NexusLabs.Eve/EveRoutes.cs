namespace NexusLabs.Eve;

internal static class EveRoutes
{
    internal const string Health = "/eve/v1/health";
    internal const string Info = "/eve/v1/info";
    internal const string CreateSession = "/eve/v1/session";

    internal static string ContinueSession(string sessionId) =>
        $"/eve/v1/session/{Uri.EscapeDataString(sessionId)}";

    internal static string StreamSession(string sessionId) =>
        $"/eve/v1/session/{Uri.EscapeDataString(sessionId)}/stream";

    internal static string CancelTurn(string sessionId) =>
        $"/eve/v1/session/{Uri.EscapeDataString(sessionId)}/cancel";

    internal static string ClearSession(string sessionId) =>
        $"/eve/v1/session/{Uri.EscapeDataString(sessionId)}/clear";

    internal static string CompactSession(string sessionId) =>
        $"/eve/v1/session/{Uri.EscapeDataString(sessionId)}/compact";

    internal static string ResetSession(string sessionId) =>
        $"/eve/v1/session/{Uri.EscapeDataString(sessionId)}/reset";
}
