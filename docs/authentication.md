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

Header precedence is:

1. Static client headers.
2. Dynamic client headers.
3. Per-turn headers.
4. Authentication headers.

Configure credential-bearing transports not to follow cross-origin redirects.
