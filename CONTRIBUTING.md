# Contributing to CAD Master

Thanks for helping. CAD Master has two parts, and contributions go to different places:

| You want to improve… | Go to |
|---|---|
| The add-in: panel, ribbon, installer, updates, SOLIDWORKS tools, crashes | **this repo** |
| *How Claude designs*: API rules, design rules, lessons from builds | the [solidworks-api skill](https://github.com/Nilove2018/solidworks-api-skill) |

## Reporting a bug
Open an issue with the **Bug report** template. Include:
- CAD Master version (*Menu > About*), SOLIDWORKS version and service pack, Windows version
- what you did, what happened, and what you expected
- the log: *Menu > Open logs*, file `cadmaster-<date>.log`. Remove anything private first
- for a SOLIDWORKS crash: the time it happened, and whether SOLIDWORKS also crashes without CAD Master (*Tools > Add-Ins*, untick CAD Master's *Start Up* box)

Please don't attach models or drawings you aren't allowed to share.

## Sharing design lessons
After a project, run `/cad:lessons` in the CAD Master chat. It writes general rules from what went wrong and what worked, with no part names or dimensions, to `Outputs\Lessons\`. Open an issue in the skill repo and paste the lessons. Built-in opt-in sharing is planned for v1.0 (see [PRIVACY.md](PRIVACY.md)).

## Building
Requirements: Windows, SOLIDWORKS 2025 (for the interop DLLs), PowerShell, and Node.js (only for the UI tests).

```powershell
.\build.ps1            # downloads the compiler and WebView2 SDK on first run, builds into bin\
.\install.ps1          # registers bin\CADMaster.dll with SOLIDWORKS (admin prompt)
```
- SOLIDWORKS locks `bin\CADMaster.dll`, so close it before rebuilding.
- The registration points at `bin\`, so after a rebuild you only need to restart SOLIDWORKS.

### Fast UI work without SOLIDWORKS
- Serve `ui\` with any local web server (for example `python -m http.server 8765` in `ui\`) and open `index.html`. It runs against `js\mock.js`. Add `?slow=6` to slow the mock build down.
- `bin\CADMaster.TestHost.exe [solidworks-pid]` runs the real panel and Claude Code in its own window, with or without SOLIDWORKS.
- Automated UI checks:
  1. set `CADMASTER_DEBUG_PORT=9444`
  2. start the test host
  3. run `node tests\cdp.js 9444 tests\host_1_start.json`

### Rules that keep SOLIDWORKS stable
These come from real crashes:
1. **Never call the SOLIDWORKS API inside a SOLIDWORKS event handler** (for example `ActiveModelDocChangeNotify`). Set a flag and handle it later from a timer.
2. **Don't start anything heavy before SOLIDWORKS has finished starting.** Wait for `StartupProcessCompleted` (see `SwAddin.StartWhenSolidWorksReady`).
3. **Test add-in start-up by launching SOLIDWORKS normally**, with the add-in set to load at start-up. `LoadAddIn` into a running SOLIDWORKS hides start-up crashes.
4. All SOLIDWORKS API calls from other threads go through `Controller.OnSw` (the main thread, with a timeout).
5. Read-only MCP tools must declare `readOnlyHint: true`, or plan mode asks permission for them.

## Pull requests
- Keep each PR focused, and describe what you tested (UI mock, test host, real SOLIDWORKS).
- Match the surrounding code style. Write UI text in plain language for designers, not programmers.
- Add a line under **[Unreleased]** in `CHANGELOG.md` saying what improved for users.
- Don't commit build output (`bin/`), downloaded dependencies (`lib/`, `tools/roslyn/`), SOLIDWORKS interop DLLs, or anything from your own projects.

By contributing you agree that your contribution is licensed under the [GNU GPL version 3](LICENSE), with the additional permission described in the README's License section.
