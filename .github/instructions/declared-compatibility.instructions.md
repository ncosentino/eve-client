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

Before declaring a newer reference, confirm the range is non-breaking from
upstream source: session routes unchanged, stream event vocabulary additive with
nothing removed, and the live probe green against that release.

The minimum supported version is a separate decision. Raising it drops support
for servers that still work, so change it only when a protocol break makes them
genuinely unusable.
