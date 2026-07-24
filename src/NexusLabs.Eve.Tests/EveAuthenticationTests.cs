using System.Text;

namespace NexusLabs.Eve.Tests;

public sealed class EveAuthenticationTests
{
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
