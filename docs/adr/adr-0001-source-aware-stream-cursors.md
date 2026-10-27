---
title: "ADR-0001: Source-aware stream cursors"
status: "Proposed"
date: "2026-10-27"
authors: []
tags: ["architecture", "decision"]
supersedes: ""
superseded_by: ""
---

# Source-aware stream cursors

## Context and scope

Eve stream protocols 21–26 store one event per source line. Protocol 27 stores
commits containing multiple facts, progress records, and transport records that
can report skipped source coverage. Counting materialized events is therefore
not a valid version-27 reconnect cursor.

The client exposes a single-use event iterator and serializable session state.
Consumers may stop between facts of one commit. Reconnecting past that line
would lose unconsumed facts; replaying it without a fact offset would repeat
already consumed facts. A delivery boundary can also precede other facts of its
own atomic commit.

This decision governs client-side event positions, persisted cursors,
reconnection and response termination. It does not migrate durable sessions
between server protocol generations or implement the Eve runtime.

## Decision drivers

- Never lose or repeat consumed facts during cursor-based resume.
- Preserve supported legacy protocols without redefining their wire format.
- Retain actual event names, owner scope and unknown payload fields.
- Keep authentication, bounded reads and reconnection in one transport pipeline.
- Do not equate transport acceptance with durable control application.

## Decision

Use version-aware source coordinates. `StreamIndex` remains an event offset for
legacy protocols and is a source-line offset for protocol 27.
`StreamEventIndex` identifies the next record within a partially consumed
version-27 line. `StreamVersion` records the observed protocol. Persist all three
with the session identifier.

Materialize each version-27 fact with its source line, record index and
end-of-line marker. Advance the saved cursor only over consumed records or
explicitly reported source coverage. A delivery boundary ends its response only
after the remainder of its commit has been consumed.

Retain raw version-27 types and scope rather than manufacturing legacy events.
Version-specific aggregation supplies the existing outcome abstraction, with
explicit interaction and delivery projections where the contracts differ.

## Alternatives considered

Keeping event-count cursors requires the least API change but skips or replays
data when one line contains several facts, so it is not correct.

Persisting only the source line matches a minimal upstream cursor and avoids a
new record offset. It requires applications to deduplicate partial-line replay
themselves, conflicting with this client's consumed-event cursor contract.

Replacing legacy APIs with a version-27-only surface simplifies implementation
but unnecessarily drops existing supported deployments and persisted cursors.
Additive coordinates and explicit version branches preserve those callers.

## Consequences

Applications must persist the complete updated state rather than deriving
positions from event counts. State merging compares line and record offsets.
Transport markers do not become public events.

Raw-event consumers must understand the version-27 vocabulary. Legacy readers
continue receiving their historical event shapes; the client does not silently
translate a new wire contract into fabricated historical facts.

A version-27 asynchronous control receipt exposes a delivery identifier, not
proof that the operation applied. Callers can read its durable outcome.

## Confirmation

Contract tests cover partial-line disposal and resume, source-position jumps,
lease reconnects, progress timestamps, fixed history bounds, complete terminal
commits and independent authenticated child reads. The real pinned server probe
must exercise source positions and durable control completion. Existing legacy
contracts remain part of the same suite.

Production session migration and application-specific raw-event consumers
cannot be certified by the library's tests.

## References

- [Published Eve 0.84 stream envelope](https://github.com/vercel/eve/blob/189e99965bf47b33c2d86d68a83094716eef100f/packages/eve/src/protocol/session-events/envelope.ts):
  defines atomic fact commits, progress records, transport coverage and source positions.
- [Published response segmentation](https://github.com/vercel/eve/blob/189e99965bf47b33c2d86d68a83094716eef100f/packages/eve/src/client/session-utils.ts):
  requires delivery correlation and drains a boundary's complete commit.
