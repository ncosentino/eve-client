---
applyTo: "version.json,.github/workflows/*.yml"
---

# Version environment isolation

Keep `cloudBuild.setVersionVariables` explicitly false. Parallel MSBuild projects
must not independently append NBGV version exports to the shared GitHub environment
file. Package and assembly version calculation remains enabled.

Workflows needing version values must obtain them explicitly with `dotnet nbgv
get-version`. Preserve the preflight guard against re-enabling implicit exports;
do not address environment-file corruption by rerunning builds or serializing the
entire solution.
