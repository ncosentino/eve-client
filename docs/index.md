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

The initial release targets eve `0.27.6`, message-stream protocol version `19`.
The client has also completed an end-to-end session against a real eve `0.24.6`
application using the same protocol version.

## Start here

Continue with [Getting Started](getting-started.md), then read
[Sessions](sessions.md) and [Streaming](streaming.md).
