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
4. Per-turn headers.
5. Authentication headers.

Configure credential-bearing transports not to follow cross-origin redirects.
