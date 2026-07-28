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

## Upstream parity radar

The repo-local `eve-client-upstream-radar` Copilot skill under
`.github/skills/` compares the declared eve release baseline with current
`vercel/eve` main. It filters to framework-neutral client and protocol changes,
checks committed `origin/main` source for an existing equivalent, and can file
deduplicated, agent-ready issues for confirmed gaps.

The skill resolves this repository from its own location, while generated
inventories and reports live under the current user's local application-data
folder. Machine-specific checkout paths and Narnia cadence configuration are
deliberately not committed.

Bounded catch-up reads (`EveStreamOptions.Follow = false`) depend on the
`includeTailIndex=1` stream query parameter and the `x-eve-stream-tail-index`
response header. eve `0.27.6` accepts the query parameter but does not report the
header, so bounded reads against that baseline fail with `EveProtocolException`
instead of silently degrading to a live follow. The compatibility probe asserts
both halves of that contract and switches to verifying a real bounded read once
the pinned server reports the header.
