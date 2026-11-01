<p align="center">
  <img src="assets/logo.png" alt="CAD Master" width="160">
</p>

<h1 align="center">CAD Master</h1>

<p align="center">
  <b>Claude Code inside SOLIDWORKS: describe a part, approve the plan, watch it get built and checked.</b>
</p>

<p align="center">
  <a href="LICENSE"><img alt="License: GPL-3.0" src="https://img.shields.io/badge/License-GPL--3.0-blue.svg"></a>
  <img alt="SOLIDWORKS 2025" src="https://img.shields.io/badge/SOLIDWORKS-2025-dc2626">
  <img alt="Claude Code" src="https://img.shields.io/badge/Claude%20Code-add--in-7c3aed">
  <img alt="Windows" src="https://img.shields.io/badge/Platform-Windows%2010%2F11-0078d4">
  <img alt="Status" src="https://img.shields.io/badge/status-pre--release-orange">
</p>

---

CAD Master is a SOLIDWORKS add-in that runs the official **Claude Code** in a task pane next to your model. You talk to it like a design engineer: "a 500 ml water bottle with a screw cap", or drop a photo and say "make this". Claude plans the design, you approve it, and then Claude builds every part in SOLIDWORKS and checks each step.

It follows the open **[solidworks-api skill](https://github.com/Nilove2018/solidworks-api-skill)**, a set of verified API rules and design lessons from real product builds. That's how it builds correct parametric parts, mated assemblies and toleranced drawings instead of guessing.

> **Status: pre-release.** It works and is in daily use, but there is no installer yet. You build it from source (below). The one-click installer, automatic updates and first-run setup are planned for **v1.0**: see [CHANGELOG](CHANGELOG.md).

## Contents
- [What you get](#what-you-get)
- [Requirements](#requirements)
- [Install (from source)](#install-from-source)
- [Using it](#using-it)
- [Privacy](#privacy)
- [How it works](#how-it-works)
- [Repository structure](#repository-structure)
- [Troubleshooting](#troubleshooting) · [Contributing](#contributing) · [License](#license)

## What you get
- **A chat written for designers, not programmers.**
  - While Claude works, one line with turning gears says what it is doing in design terms, for example *"Revolving the body · step 3 of 8"*.
  - A *hands off* tag shows while it drives SOLIDWORKS.
  - Every answer ends with the latest model view.
  - Commands and scripts stay out of the way in a separate **Details** tab.
- **Plan first.** For anything bigger than a question, Claude studies the model, asks what's unclear and shows a numbered plan. You choose:
  - **Approve, Claude builds it all**: Bypass mode; Claude makes the remaining decisions and lists its assumptions at the end.
  - **Approve, I confirm each run**.
  - **Keep planning**.
- **Tasks tab**: the approved plan becomes a live checklist that ticks off as each step is built *and verified*.
- **Drag and drop**:
  - Drop pictures, sketches, STEP/IGES/STL/SOLIDWORKS files or PDFs onto the panel.
  - Drag finished files and model views out to Explorer or an email.
- **Ribbon tab and Tools menu**: New Design Brief, Ask About Selection, Send View, Review Model, Make Drawing, Run Checks, Export STEP/STL and more.
- **Live SOLIDWORKS tools for Claude** (a local MCP server, localhost only, with a per-session token):
  - active document, selection, feature tree
  - **screenshots of the viewport**: Claude looks before it says something is right
  - rebuild and What's Wrong checks, mass and custom properties
  - in-process macros, which always ask first
- **Modes, models and effort**:
  - modes: Plan, Ask, Accept edits, Auto, Bypass
  - any Claude model
  - effort up to **Ultracode**
- **Terminal tab**: the full interactive Claude Code, with every slash command.
- **History**: resume any earlier chat.
- **Workflows** as slash commands: `/cad:plan`, `/cad:review`, `/cad:check`, `/cad:drawing`, `/cad:export`, `/cad:testrun` and `/cad:lessons`. The last one turns a finished project's failures and successes into general rules for the skill.

## Requirements
| | |
|---|---|
| Windows | 10 or 11, 64-bit |
| SOLIDWORKS | 2025 (other versions are not tested yet) |
| Claude | A Claude account: Pro, Max, Team or Enterprise, or an Anthropic Console account. Each user signs in with **their own** account |
| Claude Code | Installed automatically from the panel if missing (official installer) |
| Python | 3.9+ with `pywin32`, used by Claude's build scripts |
| WebView2 Runtime | Part of Windows 11; [download](https://developer.microsoft.com/microsoft-edge/webview2/) for Windows 10 |

## Install (from source)
1. Install the [solidworks-api skill](https://github.com/Nilove2018/solidworks-api-skill#install).
2. Clone this repo and build it. You don't need Visual Studio: the script downloads the C# compiler and the WebView2 SDK, and takes the SOLIDWORKS interop files from your SOLIDWORKS install.
   ```powershell
   git clone https://github.com/Nilove2018/cad-master-solidworks.git
   cd cad-master-solidworks
   .\build.ps1
   ```
3. Register the add-in. Windows asks once for administrator rights.
   ```powershell
   .\install.ps1
   ```
4. Start SOLIDWORKS. CAD Master loads automatically (*Tools > Add-Ins > CAD Master*). Open it from the **CAD Master** ribbon tab, or the owl icon in the task pane.

To remove it, run `.\uninstall.ps1`.

## Using it
1. **Sign in.** The panel shows a welcome screen until you're signed in. *Sign in with Claude* runs Claude Code's own `claude auth login` in your browser. CAD Master never sees your password or tokens.
2. **Describe what you want**, in Plan mode (the default for new designs). Add sizes, material and process if you know them, or use **New Design Brief** on the ribbon.
3. **Approve the plan.** Then watch the gears line and the Tasks tab. Don't click in the model while the *hands off* tag shows.
4. **Get the files.** Results appear as file chips. Click one to open it in SOLIDWORKS, or drag it out.

Tips:
- **Shift+Tab** cycles modes.
- **/** opens commands.
- **Esc** stops Claude.
- The pop-out button gives a bigger window.

## Privacy
- Your conversation goes from Claude Code to Anthropic, exactly as when you use Claude Code in a terminal, under your own account and Anthropic's terms.
- CAD Master itself sends nothing anywhere. Its tool server listens only on `localhost`, with a random token per session.
- Settings, logs and chat history stay on your PC, in `%LOCALAPPDATA%\CADMaster` and `~\.claude`.

The planned opt-in lesson sharing is described in [PRIVACY.md](PRIVACY.md). It is off unless you turn it on, and it never sends designs or files.

## How it works
```
SOLIDWORKS ── CAD Master add-in (C#, .NET 4.8)
                ├─ task pane: WebView2 panel (ui/)
                ├─ Claude Code, headless (stream-json) ── Anthropic
                │     ├─ system prompt + cad plugin (plugin/cad)
                │     └─ solidworks-api skill
                ├─ MCP server on localhost: sw_status, sw_screenshot, sw_check, …
                └─ Claude's Python build scripts attach to THIS SOLIDWORKS by process ID
```
- The add-in starts Claude Code with your account and adds a system prompt. That prompt:
  - makes Claude talk like a mechanical design engineer
  - follows the skill (test runs, parameter tables, staged builds and checks)
  - never closes or overwrites your documents
- Build scripts use the skill's `swlib`. Inside CAD Master it attaches to the SOLIDWORKS that hosts the panel and never starts a second, hidden one.
- The panel starts only after SOLIDWORKS has finished starting up, and it never calls SOLIDWORKS from inside SOLIDWORKS event handlers. Both are lessons from real crashes.

## Repository structure
| Path | What |
|---|---|
| `src/CADMaster/` | The add-in (C#, .NET Framework 4.8), in these folders: `AddIn/` ribbon and task pane; `Claude/` Claude Code sessions and history; `Mcp/` and `Sw/` the SOLIDWORKS tools; `Terminal/` the ConPTY terminal; `Ui/` the WebView2 host |
| `src/TestHost/` | The same panel in its own window, attached to a running SOLIDWORKS (for development) |
| `ui/` | The panel (HTML/CSS/JS). Opened in a normal browser, it runs against a built-in mock |
| `plugin/cad/` | Claude Code plugin: system prompt, `/cad:*` commands, `cad-reviewer` agent |
| `python/` | `cadmaster_sw.py`: attach to the hosting SOLIDWORKS by process ID |
| `icons/`, `assets/` | Ribbon and task-pane icons, logo |
| `tools/` | `fetch-deps.ps1` (compiler and WebView2 SDK), icon generator |
| `tests/` | DevTools-protocol UI tests (`cdp.js`) and the Claude Code protocol spike |
| `docs/` | Maintainer docs, for example the prompt that applies lessons to the skill |
| `build.ps1`, `install.ps1`, `uninstall.ps1` | Build, register, unregister |

## Troubleshooting
- **SOLIDWORKS closes straight away when you start it.** A hidden SOLIDWORKS with no window is still running, so the new launch hands over to it. SOLIDWORKS Rx shows "A process sldworks.exe is currently running". End `SLDWORKS.exe` in Task Manager and start again.
- **SOLIDWORKS crashes on every start after an earlier crash.** It may be trying to recover a half-written file. Move the files out of `%TEMP%\SWBackupDirectory\swxauto*` and start again.
- **Panel blank, or "couldn't start its browser view".** Check `%LOCALAPPDATA%\CADMaster\logs`. The WebView2 Runtime must be installed.
- **"Claude Code stopped".** The Details tab has the message. Run `claude auth status` in the Terminal tab.
- **Build says the DLL is in use.** Close SOLIDWORKS (and the test host) first.

## Contributing
Bug reports, ideas and pull requests are welcome: see [CONTRIBUTING.md](CONTRIBUTING.md). Improvements to *how Claude designs* (API rules and design lessons) belong in the [solidworks-api skill](https://github.com/Nilove2018/solidworks-api-skill).

## License
[GNU GPL version 3](LICENSE) © 2026 Nilove Mandal. You may use, study, change and share CAD Master, also at work. If you share a changed version, you must publish its source under GPL-3.0 too.

**Additional permission** (GPL-3.0 section 7): you may combine CAD Master with the SOLIDWORKS API interop libraries and the Microsoft Edge WebView2 SDK and Runtime, and share the result, even though their licenses are not GPL-compatible.

Bundled third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

<sub>SOLIDWORKS is a registered trademark of Dassault Systèmes SolidWorks Corporation. Claude and Claude Code are products of Anthropic. CAD Master is an independent project and is not affiliated with, endorsed by, or sponsored by Dassault Systèmes or Anthropic. Always save your work before letting automation drive a live CAD session.</sub>
