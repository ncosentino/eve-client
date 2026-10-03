---
applyTo: "**/EveProtocol.cs,**/NexusLabs.Eve.CompatibilityProbe/**/*.cs,test/fixtures/eve-agent/package.json,test/fixtures/eve-agent/*.mjs"
---

# Declared Compatibility

Rules for the constants that state which upstream eve releases this package
supports, and for the fixture and probe that verify them.

## A declared version is a claim until something asserts it

`EveProtocol.ReferenceEveVersion` states what this package supports. It observes
nothing. Interpolating it into a success message, log line, or error string
reports the claim back rather than checking it, and a green run then looks like
evidence for something no code verified.

Report the observed value, and assert it against the declared one. The probe
compares the declared constant with the version of the eve package actually
installed and exercised, so a declared bump cannot ship without the fixture
moving with it. When the declared version changes, change the fixture pin in the
same commit.

## Never let the declared reference lag implemented behavior

Advance the declared reference as soon as the last gating parity change for an
upstream release lands, and never cut a release while it lags. A package that
sends and interprets a newer protocol while declaring an older one misleads
consumers, and a published compatibility claim cannot be corrected.

Before declaring a newer reference, audit upstream routes, inspection schemas,
stream versions, and event lifetimes. Do not assume the range is additive. When
upstream replaces a contract, retain older supported contracts through explicit
version-aware handling or raise the minimum with a documented migration. Verify
the new reference through the real compatibility probe before delivery.

Read every declared protocol value from the upstream constant that owns it rather
than inferring it. The message-stream version is declared by
`EVE_MESSAGE_STREAM_VERSION` in `packages/eve/src/protocol/message.ts`, and it does
not follow from whether the event vocabulary changed: eve `0.35.0` raised it while
adding no event type and removing none.

The minimum supported version is a separate decision. Raising it drops support
for servers that still work, so change it only when a protocol break makes them
genuinely unusable.
