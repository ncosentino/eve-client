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
```

Both dynamic providers are resolved before every applicable HTTP call, including
stream reconnects. `EveRequestKind` may gain members as the upstream client adds
routes, so exhaustive switches should include a default arm.

Header precedence is:

1. Static client headers.
2. Dynamic client headers.
3. Request-aware dynamic headers.
4. Authentication headers.
5. Per-request headers.

Per-request headers are `EveSendTurnRequest.Headers` and the caller-owned request and
content headers of an `HttpRequestMessage` passed to `EveClient.SendRawAsync`. They
replace same-named client-level values case-insensitively, including `Authorization`.
This mirrors `eve@0.27.6`, where per-request headers win over client-level headers.

`RequestHeadersProvider` remains client-level. A same-named value returned there is
still overridden by the configured `IEveAuthentication`; move an intentional override
to the explicit per-turn or raw request header layer.

## Forwarding an application identity

```csharp
EveSendTurnRequest request = new()
{
    Message = EveMessageContent.FromText("Summarize my invoices."),
    Headers = new Dictionary<string, string>
    {
        ["authorization"] = endUserAuthorizationHeader,
    },
};
```

The per-turn value is used for the turn POST and for every stream connection and
reconnect of that turn. When `EveVercelOidcAuthentication` is configured, overriding
`Authorization` does not remove `x-vercel-trusted-oidc-idp-token`; that header is still
supplied by the provider unless the caller overrides that exact header too.

## Migrating from authentication-wins precedence

Releases before this change let the configured `IEveAuthentication` override per-request
headers. Before upgrading:

1. Search every `EveSendTurnRequest.Headers`, raw `HttpRequestMessage` header, and raw
   content header for `Authorization` or any authentication-owned header.
2. Remove those same-named per-request headers when the configured authentication must
   remain authoritative. They are no longer ignored.
3. Keep intentional application-user identity in `EveSendTurnRequest.Headers["authorization"]`.
4. Keep deployment authentication configured normally when using Vercel OIDC.
5. Move intentional authentication overrides out of `RequestHeadersProvider`.
6. Audit proxy and tenant-routing headers for same-named collisions and add integration
   tests asserting the intended winner.

Content-specific headers are applied only to requests with content. They are
omitted from health, info, stream, and other bodiless requests.

Configure credential-bearing transports not to follow cross-origin redirects.
