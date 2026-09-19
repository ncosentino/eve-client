namespace NexusLabs.Eve.Tests;

public sealed class EveUrlBuilderTests
{
    [Test]
    public async Task Create_PreservesBasePathAndOverridesQueryParameter()
    {
        Uri uri = EveUrlBuilder.Create(
            "https://agent.example.com/api?token=secret&startIndex=stale",
            "/eve/v1/session/session_1/stream",
            new Dictionary<string, string>
            {
                ["startIndex"] = "4",
            });

        await Assert.That(uri.ToString()).IsEqualTo(
            "https://agent.example.com/api/eve/v1/session/session_1/stream?token=secret&startIndex=4");
    }

    [Test]
    public async Task Create_SupportsRelativeProxyPrefix()
    {
        Uri uri = EveUrlBuilder.Create(
            "/api?token=secret",
            "/eve/v1/session/session_1/stream",
            new Dictionary<string, string>
            {
                ["startIndex"] = "-1",
            });

        await Assert.That(uri.OriginalString).IsEqualTo(
            "/api/eve/v1/session/session_1/stream?token=secret&startIndex=-1");
    }

    [Test]
    public async Task Create_SupportsSameOriginRoot()
    {
        Uri uri = EveUrlBuilder.Create(string.Empty, "/eve/v1/health");

        await Assert.That(uri.OriginalString).IsEqualTo("/eve/v1/health");
    }

    [Test]
    [Arguments("/eve/support", "/eve/v1/info", "/eve/support/v1/info")]
    [Arguments("/eve/support", "/eve/v1/session", "/eve/support/v1/session")]
    [Arguments(
        "/eve/support",
        "/eve/v1/session/session_1",
        "/eve/support/v1/session/session_1")]
    [Arguments(
        "https://app.example.com/eve/support",
        "/eve/v1/info",
        "https://app.example.com/eve/support/v1/info")]
    [Arguments(
        "https://app.example.com/eve/support",
        "/eve/v1/session",
        "https://app.example.com/eve/support/v1/session")]
    [Arguments(
        "https://app.example.com/eve/support",
        "/eve/v1/session/session_1",
        "https://app.example.com/eve/support/v1/session/session_1")]
    [Arguments(
        "https://app.example.com/eve/support/v1",
        "/eve/v1/session",
        "https://app.example.com/eve/support/v1/session")]
    public async Task Create_MapsProtocolRoutesOntoCompactNamedAgentMount(
        string host,
        string route,
        string expected)
    {
        Uri uri = EveUrlBuilder.Create(host, route);

        await Assert.That(uri.ToString()).IsEqualTo(expected);
    }

    [Test]
    [Arguments("/api/eve/support", "/api/eve/support/eve/v1/info")]
    [Arguments("/eve/Support", "/eve/Support/eve/v1/info")]
    [Arguments("/eve/support/extra", "/eve/support/extra/eve/v1/info")]
    public async Task Create_PreservesOrdinaryPrefixesThatAreNotCompactMounts(
        string host,
        string expected)
    {
        Uri uri = EveUrlBuilder.Create(host, "/eve/v1/info");

        await Assert.That(uri.ToString()).IsEqualTo(expected);
    }

    [Test]
    public async Task Create_PreservesEmbeddedRouteQueryForAbsoluteHost()
    {
        Uri uri = EveUrlBuilder.Create(
            "https://agent.example.com",
            "/eve/v1/callback/tok_1?code=ok&state=xyz");

        await Assert.That(uri.ToString()).IsEqualTo(
            "https://agent.example.com/eve/v1/callback/tok_1?code=ok&state=xyz");
    }

    [Test]
    public async Task Create_PreservesEmbeddedRouteQueryForRelativeProxyPrefix()
    {
        Uri uri = EveUrlBuilder.Create("/api", "/eve/v1/callback/tok_1?code=ok");

        await Assert.That(uri.OriginalString).IsEqualTo(
            "/api/eve/v1/callback/tok_1?code=ok");
    }

    [Test]
    public async Task Create_MergesHostQueryAheadOfEmbeddedRouteQuery()
    {
        Uri uri = EveUrlBuilder.Create(
            "https://agent.example.com/api?token=secret",
            "/eve/v1/callback/tok_1?code=ok");

        await Assert.That(uri.ToString()).IsEqualTo(
            "https://agent.example.com/api/eve/v1/callback/tok_1?token=secret&code=ok");
    }

    [Test]
    public async Task Create_ExplicitQueryReplacesEmbeddedRouteQuery()
    {
        Uri uri = EveUrlBuilder.Create(
            "https://agent.example.com",
            "/eve/v1/callback/tok_1?code=embedded&state=xyz",
            new Dictionary<string, string>
            {
                ["code"] = "explicit",
            });

        await Assert.That(uri.ToString()).IsEqualTo(
            "https://agent.example.com/eve/v1/callback/tok_1?code=explicit&state=xyz");
    }

    [Test]
    public async Task Create_ExplicitQueryReplacesEmbeddedRouteQueryForRelativeProxyPrefix()
    {
        Uri uri = EveUrlBuilder.Create(
            "/api",
            "/eve/v1/session/session_1/stream?startIndex=0",
            new Dictionary<string, string>
            {
                ["startIndex"] = "7",
            });

        await Assert.That(uri.OriginalString).IsEqualTo(
            "/api/eve/v1/session/session_1/stream?startIndex=7");
    }
}
