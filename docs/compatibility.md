---
description: Understand supported eve versions, stream protocol compatibility, and preview-version policy.
---

# Compatibility

| NexusLabs.Eve | Reference eve | Stream protocol | Status |
|---|---:|---:|---|
| 0.1.x | 0.27.6 | 19 | Primary compatibility target |
| 0.1.x | 0.24.6 | 19 | End-to-end verified with `bg-eve` |

eve remains preview software. Package upgrades should therefore validate both:

1. The public HTTP route and body contracts.
2. The durable message-stream protocol version and event shapes.

The repository contains a pinned eve `0.27.6` fixture with a deterministic
model. CI builds the real server and verifies health, info, text turns,
attachment staging, streaming, cooperative cancellation, and session reset
through the C# client.

Upstream eve 0.27.6 lets generic per-request headers replace authentication.
NexusLabs.Eve requires an explicit client allowlist and dedicated per-call override
for protected headers so existing generic header bags cannot silently replace credentials.

Unknown event types remain available through `EveStreamEvent.Type` and `Data`
instead of causing deserialization failure.
