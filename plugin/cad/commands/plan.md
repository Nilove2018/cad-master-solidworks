---
description: Plan a new SOLIDWORKS design (brief, questions, numbered checkable plan) before building anything
argument-hint: <what to design>
---
Plan this design before building anything: $ARGUMENTS

1. Load the `solidworks-api` skill. If the brief refers to the open model, look at it with `mcp__solidworks__sw_status`, `sw_feature_tree` and `sw_screenshot`.
2. Check the input dimensions (skill rule 1.11). List what is ambiguous and ask me with AskUserQuestion, at most 4 questions with concrete options.
3. Write the plan as numbered steps that can each be checked:
   - parameters file and fit/clearance table (rule 1.17)
   - test runs for any behaviour that isn't [PROVEN] (rule 1.14)
   - feature groups in build order, with the names you will give them
   - checks after each step (section 12), then outputs (files, renders, exports, drawings)
   - open items I must measure or decide
4. Present it for approval (ExitPlanMode). Don't change any file or model before I approve.
