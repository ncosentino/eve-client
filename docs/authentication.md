---
description: Configure bearer, HTTP Basic, Vercel OIDC, and dynamic request headers.
---

# Authentication

Credentials are resolved immediately before every HTTP request, including
stream reconnects.

## Bearer authentication

```csharp
Authentication = new EveBearerAuthentication(
    cancellationToken => GetAccessTokenAsync(cancellationToken));
```

## HTTP Basic authentication

```csharp
Authentication = new EveBasicAuthentication(
    "agent-client",
    cancellationToken => GetPasswordAsync(cancellationToken));
```

## Vercel OIDC

```csharp
Authentication = new EveVercelOidcAuthentication(
    cancellationToken => GetVercelOidcTokenAsync(cancellationToken));
```

Vercel OIDC emits both `Authorization: Bearer ...` and
`x-vercel-trusted-oidc-idp-token`.

## Dynamic headers

```csharp
HeadersProvider = async cancellationToken => new Dictionary<string, string>
{
    ["x-vercel-protection-bypass"] =
        await GetProtectionBypassAsync(cancellationToken),
};
```

Use `RequestHeadersProvider` when a dynamic header belongs to one HTTP operation.
The immutable context exposes a stable `EveRequestKind`, so bootstrap credentials can
be limited to session creation without relying on provider invocation order:

```csharp
RequestHeadersProvider = (context, cancellationToken) =>
    ValueTask.FromResult<IReadOnlyDictionary<string, string>>(
        context.Kind == EveRequestKind.CreateSession
            ? new Dictionary<string, string>
            {
                ["x-session-bootstrap"] = encryptedBootstrapCredential,
            }
            : new Dictionary<string, string>());

ProtectedHeaderNames = ["x-session-bootstrap"];
```

Both dynamic providers are resolved before every applicable HTTP call, including
stream reconnects. `EveRequestKind` may gain members as the upstream client adds
routes, so exhaustive switches should include a default arm.

Header resolution is:

1. Static client headers.
2. Dynamic client headers.
3. Request-aware dynamic headers.
4. Authentication headers.
5. Generic per-request headers for names that are not protected.
6. Explicit protected-header overrides allowed by client policy.

Per-request headers are `EveSendTurnRequest.Headers` and the caller-owned request and
content headers of an `HttpRequestMessage` passed to `EveClient.SendRawAsync`. They
replace same-named non-protected client-level values case-insensitively.

Every name declared by `IEveAuthentication.AuthenticationHeaderNames`, every header
emitted by the authentication provider, and every name in
`EveClientOptions.ProtectedHeaderNames` is protected. Generic per-request values with
those names are ignored.

`RequestHeadersProvider` remains client-level. A same-named value returned there is
still overridden by the configured `IEveAuthentication`. Credentials supplied through
that provider should also be listed in `ProtectedHeaderNames`.

## Explicitly forwarding an application identity

```csharp
EveClientOptions options = new("https://agent.example.com")
{
    Authentication = new EveVercelOidcAuthentication(GetDeploymentTokenAsync),
    AllowedProtectedHeaderOverrides = ["authorization"],
};

EveSendTurnRequest request = new()
{
    Message = EveMessageContent.FromText("Summarize my invoices."),
    ProtectedHeaderOverrides = new Dictionary<string, string>
    {
        ["authorization"] = audienceRestrictedForwardedIdentityToken,
    },
};
```

Both conditions are required: the client must allow the header name, and the individual
turn must use `ProtectedHeaderOverrides`. `EveSendTurnRequest.Headers` can never replace
a protected credential.

The override is used for the turn POST and every stream connection and reconnect of that
turn. It does not flow to `CancelAsync`, `ResetAsync`, or a separate `StreamAsync`
attachment. It also does not leak into later turns.

When `EveVercelOidcAuthentication` is configured, overriding `Authorization` does not
remove `x-vercel-trusted-oidc-idp-token`. That deployment credential remains protected
unless its exact name is independently allowlisted and explicitly overridden.

Do not forward a bearer token minted for an unrelated API audience. Prefer a distinct,
short-lived token intended for the eve agent, and require the agent to validate that
forwarded identity independently. A protected-header override is not itself an
authorization boundary.

## Raw request overrides

```csharp
using HttpResponseMessage response = await client.SendRawAsync(
    request,
    new EveRawRequestOptions
    {
        ProtectedHeaderOverrides = new Dictionary<string, string>
        {
            ["authorization"] = audienceRestrictedForwardedIdentityToken,
        },
    },
    cancellationToken);
```

Headers placed on `HttpContent.Headers` are always generic. They cannot replace protected
credentials, even when that name is allowlisted.

## Migration

Existing consumers keep authentication-authoritative behavior by default. To opt into the
eve 0.27.6 identity-forwarding use case:

1. Identify the exact protected header that must be replaceable.
2. Add only that name to `AllowedProtectedHeaderOverrides`.
3. Move the trusted value from `EveSendTurnRequest.Headers` or raw request headers into the
   dedicated `ProtectedHeaderOverrides` property.
4. Never copy an inbound request-header collection into the protected override dictionary.
5. List credentials supplied outside `IEveAuthentication` in `ProtectedHeaderNames`.
6. Add integration tests for POST, stream reconnect, later-turn, and cancellation behavior.

Content-specific headers are applied only to requests with content. They are
omitted from health, info, stream, and other bodiless requests.

Configure credential-bearing transports not to follow cross-origin redirects.
