---
description: Run the solidworks-api section 12 verification checklist on the active document
---
Run the `solidworks-api` skill section 12 verification checklist on the active SOLIDWORKS document. Don't change the model.

- Start with `mcp__solidworks__sw_check` (rebuild: true) and `sw_feature_tree`.
- Parts: body count, volume, exact extents with `GetExtremePoint` (skill section 9), and under-defined sketches.
- Assemblies: every component's constraint status and mate errors. Run interference detection twice, with coincidence off and then on (skill section 12), and classify every hit as intended or a clash.
- Report a table: check, result, number, pass/fail. Then list what must be fixed.
