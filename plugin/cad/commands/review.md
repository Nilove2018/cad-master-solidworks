---
description: Review the active SOLIDWORKS document without changing it
argument-hint: [focus, e.g. printability]
---
Review the active SOLIDWORKS document without changing anything. Focus: $ARGUMENTS

1. Load the `solidworks-api` skill.
2. Gather facts with `mcp__solidworks__sw_status`, `sw_feature_tree`, `sw_check`, `sw_mass_properties` and an isometric `sw_screenshot`.
3. Report, most important first:
   - rebuild errors, What's Wrong, under-defined sketches, suppressed or broken features
   - design intent: naming, global variables, parametric robustness (skill rules 1.6 and 1.9)
   - manufacturability for the likely process (walls, overhangs, draft, clearances: skill section 14)
   - for assemblies: constraint status, mate errors, and whether interference detection is needed
4. End with a short list of fixes I can approve one by one.
