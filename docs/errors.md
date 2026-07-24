---
description: Distinguish HTTP failures, malformed protocol responses, and terminal eve session failures.
---

# Errors

## HTTP failures

Non-successful routes throw `EveClientException`. It exposes:

- The HTTP status code.
- The raw response body.
- Normalized response headers.

When the body contains `{ "error": "..." }`, that value becomes the exception
message.

## Protocol failures

Successful responses that do not satisfy the expected eve contract throw
`EveProtocolException`.

## Session failures

A streamed `session.failed` event is part of the protocol, not a transport
exception. `GetOutcomeAsync` returns `EveTurnStatus.Failed` and preserves the
failure event in `Events`.

## Local cancellation

Cancelling stream enumeration detaches the caller but does not stop the durable
turn. Call `EveSession.CancelAsync` to request cooperative server-side
cancellation, then continue consuming the stream through its waiting boundary.
