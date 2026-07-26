using System.Text;

namespace NexusLabs.Eve.Tests;

public sealed class EveAuthenticationTests
{
    [Test]
    public async Task BuiltInProviders_DeclareOwnedHeaderNames()
    {
        EveBearerAuthentication bearer = new("token");
        EveBasicAuthentication basic = new("agent", "password");
        EveVercelOidcAuthentication oidc = new("oidc-token");

        await Assert.That(bearer.AuthenticationHeaderNames.Count).IsEqualTo(1);
        await Assert.That(bearer.AuthenticationHeaderNames.Contains("authorization")).IsTrue();
        await Assert.That(basic.AuthenticationHeaderNames.Count).IsEqualTo(1);
        await Assert.That(basic.AuthenticationHeaderNames.Contains("authorization")).IsTrue();
        await Assert.That(oidc.AuthenticationHeaderNames.Count).IsEqualTo(2);
        await Assert.That(oidc.AuthenticationHeaderNames.Contains("authorization")).IsTrue();
        await Assert.That(oidc.AuthenticationHeaderNames.Contains(
            EveProtocol.VercelTrustedOidcTokenHeaderName)).IsTrue();
    }

    [Test]
    public async Task VercelOidc_EmitsBearerAndTrustedTokenHeaders(
        CancellationToken cancellationToken)
    {
        EveVercelOidcAuthentication authentication = new("oidc-token");

        IReadOnlyDictionary<string, string> headers = await authentication.GetHeadersAsync(
            cancellationToken);

        await Assert.That(headers.Count).IsEqualTo(2);
        await Assert.That(headers["authorization"]).IsEqualTo("Bearer oidc-token");
        await Assert.That(headers[EveProtocol.VercelTrustedOidcTokenHeaderName])
            .IsEqualTo("oidc-token");
    }

    [Test]
    public async Task Bearer_ResolvesFreshTokenForEachRequest(CancellationToken cancellationToken)
    {
        int resolution = 0;
        EveBearerAuthentication authentication = new(_ =>
        {
            resolution++;
            return ValueTask.FromResult($"token-{resolution}");
        });

        IReadOnlyDictionary<string, string> first = await authentication.GetHeadersAsync(
            cancellationToken);
        IReadOnlyDictionary<string, string> second = await authentication.GetHeadersAsync(
            cancellationToken);

        await Assert.That(first["authorization"]).IsEqualTo("Bearer token-1");
        await Assert.That(second["authorization"]).IsEqualTo("Bearer token-2");
    }

    [Test]
    public async Task Basic_UsesUtf8Credentials(CancellationToken cancellationToken)
    {
        EveBasicAuthentication authentication = new("agent", "pässword");

        IReadOnlyDictionary<string, string> headers = await authentication.GetHeadersAsync(
            cancellationToken);
        string encoded = headers["authorization"]["Basic ".Length..];
        string credentials = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));

        await Assert.That(credentials).IsEqualTo("agent:pässword");
    }
}
