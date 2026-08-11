---
description: Distinguish HTTP failures, malformed protocol responses, and terminal eve session failures.
---

# Errors

## HTTP failures

Non-successful routes throw `EveClientException`. It exposes:

- The HTTP status code.
- `ErrorCode`, the server's stable machine-readable code, when present.
- The raw response body.
- Normalized response headers.

When the body contains `{ "error": "..." }`, that value becomes the exception
message.

## Branch on a stable error code

eve `0.31.0` reports a stable `code` alongside the human-readable message. Branch
on `ErrorCode` instead of matching message text, which is not a contract:

```csharp
try
{
    await session.SendAsync("Continue.", cancellationToken);
}
catch (EveClientException exception)
    when (exception.ErrorCode == "session_not_active")
{
    EveSession replacement = client.CreateSession();
}
```

`session_not_active` accompanies HTTP 409 when a turn targets a session that was
reset or is otherwise no longer active. `ErrorCode` is the raw server string, so
a code this client does not model stays observable rather than being discarded,
and it is `null` when the response carried none.

## Protocol failures

Successful responses that do not satisfy the expected eve contract throw
`EveProtocolException`.

## Version mismatch

A client and agent on opposite sides of the eve `0.31.0` cutover accept the first
turn and fail the second, because the first turn carries no continuation token in
either direction. These `EveClientException` messages identify a mismatch rather
than an application error:

| Message | Meaning |
|---|---|
| HTTP 400 `Missing or empty 'continuationToken' field.` | The agent predates eve `0.31.0` and requires a token this package no longer sends. |
| HTTP 400 `Session-ID routes do not accept 'continuationToken'.` | The agent is eve `0.31.0` or newer and the caller is on `0.1.0-alpha.3` or earlier. |
| HTTP 404 `Cannot find any route matching ... /clear`, `/compact`, or `/reset` | The agent predates eve `0.31.0`, so the identifier-addressed control routes do not exist. |

A control operation sent to the old fixed paths does **not** report 404 on eve
`0.31.x`. `/eve/v1/session/clear` matches the continue route with a session
identifier of `clear`, so it is misrouted and reports HTTP 400 about missing
message content instead.

Upgrade the client and the agent together. See [Migration](migration.md).

## Session failures

A streamed `session.failed` event is part of the protocol, not a transport
exception. `GetOutcomeAsync` returns `EveTurnStatus.Failed` and preserves the
failure event in `Events`.

## Local cancellation

Cancelling stream enumeration detaches the caller but does not stop the durable
turn. Call `EveSession.CancelAsync` to request cooperative server-side
cancellation, then continue consuming the stream through its waiting boundary.
