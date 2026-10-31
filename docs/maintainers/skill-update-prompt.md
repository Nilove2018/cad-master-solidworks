# Prompt: apply project lessons to the solidworks-api skill

For the skill maintainer. Paste this in a Claude Code session opened in your clone of the [solidworks-api-skill](https://github.com/Nilove2018/solidworks-api-skill) repo. Then attach or name the lessons files, or the accepted issues in the lessons repo.

---

Update the solidworks-api skill with the lessons in these files:
<paths to one or more `Outputs\Lessons\*.md` files>

Each file has three kinds of entries: rules from mistakes (L1, L2, …), a diagnosis playbook for problems that were solved (D1, D2, …) and proven techniques (P1, P2, …).

For each lesson (L1, L2, …):
1. Read the skill section it names, and search the whole skill for related text. The rule may already exist elsewhere, or conflict with something.
2. Decide:
   - **apply**: add, correct or clarify the rule
   - **merge**: fold it into an existing rule, keeping it general
   - **reject**: it is project-specific, duplicates an existing rule, or isn't proven. Say why.
3. Keep every rule general: no project part names, dimensions or paths. Keep the skill's own style, numbering and section layout. Correct a WRONG rule in place, don't add a contradicting one.
4. API rules: keep the verified snippet minimal, and check the call and swconst values against the API docs and `swlib.py`. If a lesson proposes a swlib helper, add it to `examples\swlib.py` with a docstring, and run a quick test run in SOLIDWORKS if it is open.
5. IGNORED lessons (the rule existed but was missed): make it harder to miss rather than repeating it. Add it to the section 12 checklist, a pre-flight list, or a swlib guard that raises an error.

For each **diagnosis playbook** entry (D1, D2, …):
- Add it to a "Troubleshooting playbook" in the skill: symptom, then the first diagnostic to run, then likely causes, then the fix. Create that section if it doesn't exist.
- Group it with similar problems, so the first check for a new problem of that kind is obvious.

For each **proven technique** (P1, P2, …):
- **PROVEN**: add it to the matching section as a recommended technique, with its evidence.
- **BETTER**: replace or extend the skill's current approach. Keep the old one only if it is still right in other cases, and say when to use which.
- **CONFIRMS**: don't add text. Only adjust the rule if its wording was hedged and is now proven.

Then:
- Update every copy of the skill: the repo's `skills/solidworks-api/SKILL.md` (the source of truth; run `python tools/sync_skill.py` for the root copy) and, if you keep one, your personal copy in `~/.claude/skills/solidworks-api/`. Keep the rules identical; only machine-specific notes may differ.
- Bump `version` in `.claude-plugin/plugin.json` and in the skill header. Add a CHANGELOG line per applied lesson; users see these in CAD Master as "what improved". Publish a GitHub release so CAD Master users get it automatically.
- Report a table with these columns: lesson, decision (applied, merged or rejected), section and one-line reason. Also show the diff.

Don't commit until I've reviewed the diff.
