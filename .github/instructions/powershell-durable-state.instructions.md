---
applyTo: "**/*.ps1"
---

# PowerShell Durable State

Rules for any script that reads or writes a persisted document such as JSON state.

## Normalize values on read, because deserialization mutates them

`ConvertFrom-Json` converts an ISO-8601 timestamp string into a local `DateTime`.
Handing that value back to `ConvertTo-Json` re-serializes the same instant with the
current machine's offset, so a read-then-write cycle silently rewrites records the run
never intended to touch. The values still parse, so nothing fails.

Normalize a deserialized value to its canonical form as it is read, before it can reach
a writer. Never assume a value is unchanged just because the script only passed it
through.

```powershell
# WRONG - rewrites every timestamp in the file with this machine's offset
$state = Get-Content $path -Raw | ConvertFrom-Json
$state | ConvertTo-Json | Set-Content $path
```

## Verify the persisted artifact, not the return value

A write that returns successfully proves nothing about what landed on disk. Confirm a
state change by reading the persisted text back and inspecting both the fields you
claim to have changed and the ones you claim to have left alone. A round-trip that
alters an untouched record is a defect even when every value still parses.

Cover this with a test that asserts an existing record survives a read-then-write cycle
unchanged, not only that a new record can be written.

## A writer must carry forward what it does not own

Rebuilding a state object to update one section silently drops every property the code
does not restate. Adding a field to persisted state therefore requires auditing every
existing writer, not just the new one. Prefer updating the fields a function owns over
reconstructing the whole document.

When a persisted schema version changes, accept and upgrade the older shape rather than
rejecting it; a state file that is still valid must not become unreadable.
