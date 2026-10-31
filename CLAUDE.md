# CLAUDE.md: working on the CAD Master repository

This repo is the **CAD Master SOLIDWORKS add-in**: Claude Code in a SOLIDWORKS task pane. You are maintaining the add-in, not designing CAD parts (unless the user asks). Rules for *how Claude designs* live in the separate solidworks-api skill repo.

## Layout
| Path | What |
|---|---|
| `src/CADMaster/` | C# add-in (.NET Framework 4.8, x64). `AddIn/SwAddin.cs` (ISwAddin, ribbon, task pane, start-up gating), `Controller.cs` (UI messages, Claude sessions, files in/out), `Claude/` (headless stream-json session, history), `Mcp/` + `Sw/` (localhost MCP server, SOLIDWORKS tools), `Terminal/` (ConPTY), `Ui/PanelControl.cs` (WebView2 host) |
| `ui/` | Panel HTML/CSS/JS; `js/app.js` is the app, `js/mock.js` fakes the host in a normal browser |
| `plugin/cad/` | Claude Code plugin loaded with `--plugin-dir`: `system-prompt.md` (has `{{PLACEHOLDERS}}` filled by `Controller.SystemPrompt`), `commands/`, `agents/` |
| `tests/` | `cdp.js` runs JSON step files against the panel over the DevTools protocol |
| `notes/` | Local, untracked planning notes |

## Build and test
- `.\build.ps1`: needs SOLIDWORKS installed; fetches the compiler and WebView2 SDK; output in `bin\`. Fails if SOLIDWORKS has the DLL loaded.
- UI changes: preview with the mock (serve `ui\`), then the test host. Add-in changes: a real SOLIDWORKS **launched normally** (start-up crashes don't show with `LoadAddIn`).
- JSON uses `JavaScriptSerializer` (`Util/Json.cs`), not Newtonsoft: SOLIDWORKS ships its own Newtonsoft version.

## Rules
1. Never call the SOLIDWORKS API inside SOLIDWORKS event handlers; flag the event and handle it from the context timer.
2. Start nothing heavy before `StartupProcessCompleted` (`SwAddin.StartWhenSolidWorksReady`).
3. SOLIDWORKS API calls from other threads must go through `Controller.OnSw`.
4. UI text is for mechanical designers: design terms, no code in the chat. Technical detail goes in the Details tab.
5. Nothing may leave the user's PC except through Claude Code (to Anthropic) and what `PRIVACY.md` lists. Keep `PRIVACY.md` true.
6. Never commit `bin/`, `lib/`, `tools/roslyn/`, SOLIDWORKS interop DLLs, `notes/`, `design/`, or anything from a user's own projects.
7. User-visible changes get a plain-language line in `CHANGELOG.md` under **[Unreleased]**. The update dialog shows these lines.
