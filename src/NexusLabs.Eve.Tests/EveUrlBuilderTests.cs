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
}
