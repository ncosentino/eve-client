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

This release requires eve `0.54.2` or newer and targets eve `0.63.0`, using
message-stream protocol `25`, stream-control protocol `1`, and agent-info schema `v4`.
It strictly accepts only the
`subagent-call`, `task-cancel`, and `workflow-tool-call` kernel-effect actions while
accepting schema-v4 responses both with and without legacy `workflow` metadata. Earlier
schema-v4 servers can advertise the obsolete `task-update` action without a schema-version
discriminator, so upgrade the server before this client. Eve `0.52.3` remains the
historical delivery-correlation boundary for existing-session sends; initial session
creation and `RespondAsync` human-input continuation do not require that correlation. See
[Compatibility](compatibility.md) and [Migration](migration.md).

## Start here

Continue with [Getting Started](getting-started.md), then read
[Sessions](sessions.md) and [Streaming](streaming.md).
