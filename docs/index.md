---
description: Use Vercel eve agents from .NET with durable sessions, streaming, cancellation, and structured output.
---

# NexusLabs.Eve

![eve.NET logo](assets/eve-brand.png){ width="320" }

`NexusLabs.Eve` is a .NET client for the stable HTTP protocol exposed by
[Vercel eve](https://github.com/vercel/eve).

It provides:

- Health and agent inspection.
- Bearer, Basic, and Vercel OIDC authentication.
- Durable session continuation and persistence.
- NDJSON event streaming with reconnect-by-index.
- Cooperative turn cancellation.
- Attachments, human-input responses, and structured output.

The package is intentionally transport-focused. JavaScript UI helpers from
`eve/client`, such as React/Vue/Svelte integrations and `EveAgentStore`, are not
part of the .NET API.

## Compatibility

This release requires and targets eve `0.52.3`, using message-stream protocol `25` and
agent-info schema `v4`. Existing-session message sends require the accepted response's
`deliveryId` so stale durable events can be consumed without being returned as the new
turn. Pre-`0.52.3` servers omit that field, so upgrade the server before this client.
Initial session creation and `RespondAsync` human-input continuation do not require
delivery correlation. See [Compatibility](compatibility.md) and
[Migration](migration.md).

## Start here

Continue with [Getting Started](getting-started.md), then read
[Sessions](sessions.md) and [Streaming](streaming.md).
