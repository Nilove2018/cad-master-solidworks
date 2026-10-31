---
description: After a project, turn its difficulties into general design and API rules for the solidworks-api skill
argument-hint: [project name or focus, optional]
---
The project is finished (or paused). Do a lessons-learned review of this whole conversation, so the solidworks-api skill can be improved and future designs avoid the same mistakes. Project / focus: $ARGUMENTS

Do not change any model or file except the lessons file below.

## 1. Collect what went wrong
Go through the entire conversation, including the Details of every tool call. List each difficulty:
- failed or retried scripts and API calls
- wrong results that a check caught: dimensions, mates, interference, What's Wrong, rebuild errors
- SOLIDWORKS crashes, hangs, "server threw an exception", dialogs that blocked
- design decisions you had to revise, things the user corrected, and assumptions that turned out wrong
- anything that took more than one attempt

For each one note:
- **symptom**: what was seen
- **root cause**: why it really happened
- **fix**: what solved it
- **attempts**: roughly how much time or how many tries it cost
- **how it was solved**: the path from symptom to fix:
  - which probe, measurement, test run or screenshot revealed the real cause
  - which ideas were tried and failed, and why
  - what finally made the difference

  This is the reusable part: next time a similar new problem appears, this diagnosis method should be the first thing tried.

## 1b. Collect what went right
Lessons come from success as well as failure. List the things that worked well, especially in work that was new (not yet covered by the skill):
- techniques, API calls or modelling approaches that worked first time, or worked after the skill's approach failed
- good diagnosis methods: a probe script, a ray-cast measurement, a drag test, a screenshot comparison, a test run that isolated the problem quickly
- design decisions the user liked, or that passed every check without rework (proportions, fits, clearances, wall thicknesses, mate schemes, feature order)
- workflow that saved time, such as building order, parameter tables or check order

For each one note:
- **task**: what was being done
- **what worked**
- **why it worked**
- **evidence**: the check or measurement that proved it

## 2. Check the skill
Load the `solidworks-api` skill and read the sections that relate to each difficulty. Classify each one:
- **NEW**: the skill doesn't cover it.
- **WRONG**: the skill says something that is incorrect or caused the problem.
- **UNCLEAR**: the skill covers it, but not clearly enough to have prevented the mistake.
- **IGNORED**: the skill already covers it clearly and it simply wasn't followed. Note what would make it harder to miss: a checklist item, a swlib guard, or a pre-flight check.
- **PROJECT-ONLY**: specific to this one design, with no general lesson. Drop these.

Classify each success from 1b the same way:
- **PROVEN**: a new technique or diagnosis method worth adding to the skill.
- **CONFIRMS**: the skill's existing rule worked. Note it in one line; it raises confidence in that rule.
- **BETTER**: it worked better than what the skill recommends. Propose replacing or extending the skill's approach.

## 3. Turn each lesson into a GENERAL rule
Each rule must hold for any design, not just this one:
- No part names, project dimensions or file paths from this project. Write "a revolved thin-walled body", not "the WB500 bottle".
- State it as a rule with its reason. For example: "When X, do Y, because Z."
- Where it is about the API, give the exact call, signature, swconst value or swlib helper, plus a minimal verified snippet. Only include code that actually worked in this session.
- Where it is about design (manufacturing, fits, threads, draft, wall thickness, assembly, mates, aesthetics), give the number or criterion and its source if it is a standard.
- Say how to detect the problem early: which check, test run or measurement would have caught it.
- Name the skill section it belongs in, and whether it adds a rule, corrects one, or adds a checklist item to section 12.

## 4. Write the lessons file
Save the file as `Outputs\Lessons\<YYYY-MM-DD> <project>.md` in the workspace, using this structure:

```
# Lessons: <project> (<date>)
SOLIDWORKS <version> · Claude Code <version> · skill version used: <from the skill header>

## Summary
<2-4 lines: what was built, how many difficulties, the biggest time sinks>

## Rules for the skill
### L1. <short rule title>
- Type: NEW | WRONG | UNCLEAR | IGNORED
- Skill section: <number and name>   Change: add rule | correct rule | add checklist item | add swlib helper
- Rule: <the general rule, imperative, with the reason>
- Detect early: <check or test run>
- Evidence: <one line: what happened in this project, symptom → root cause → fix>
- Verified snippet (API rules only):
  <minimal code that worked>

### L2. ...

## How new problems were solved (diagnosis playbook)
### D1. <kind of problem, in general terms, e.g. "mate solves but motion goes the wrong way">
- First sign: <symptom>
- Diagnosis that found the cause: <probe, measurement or test run, and what it showed>
- Dead ends: <what was tried and didn't help, and why>
- Fix: <general fix>
- Skill section: <where this playbook entry belongs>
- Verified snippet (if a probe script was the key):
  <minimal code>

## Proven techniques (what worked)
### P1. <short title>
- Type: PROVEN | BETTER | CONFIRMS
- Task: <general task, e.g. "threaded closure on a revolved body">
- What worked and why: <general description>
- Evidence: <check result with numbers>
- Skill section: <number and name>   Change: add technique | replace approach | note as confirmed
- Verified snippet (API techniques only):
  <minimal code that worked>

## Proposed swlib changes
<functions to add or fix, with signature and purpose; only if a lesson needs one>

## Dropped (project-only)
<one line each, so nothing is silently lost>
```

## 5. Report
In the chat, give a short engineer-style summary:
- how many lessons, by type
- the top 3 that would have saved the most time
- the most useful new diagnosis method, and the best proven technique
- the lessons file as a full path in backticks, so the user can drag it out

Don't edit the skill itself here. The user reviews the file and applies it to the skill separately.
