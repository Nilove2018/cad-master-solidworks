# Changelog

Each version lists **what improved** in plain words. CAD Master will show these notes when an update is available.

## [Unreleased] (towards 1.0.0)
### Added
- **Setup checklist**: opens the first time CAD Master starts, and whenever something it needs is missing. It checks Claude Code, your Claude account, Python, pywin32, Git, the design rules and SOLIDWORKS itself. Each row has a button that installs or fixes it, and the SOLIDWORKS test builds and checks a small block, then closes it without saving. Open it again any time from the menu or About: **Setup check**.
- **Updates inside CAD Master**: every few hours CAD Master looks for a new version and installs it by itself. The download is checked against its published checksum and installs next to the running version, so SOLIDWORKS can stay open; it loads at the next SOLIDWORKS start, which then says what improved. **Check for updates** (menu and About) checks CAD Master and the design rules together and installs whatever is new. About also has **Roll back** to the version before. No administrator prompt. In Settings you can turn off the automatic install (a gold bar then offers **What's new** and **Update now**), the automatic check, or get test versions.
- **The design rules update themselves**: every few hours, between chats, CAD Master updates the solidworks-api skill through Claude Code's own plugin system. If Claude is working, the update runs as soon as it has finished. A toast says *"Design rules updated to 2.4.0"*, and the Details tab and About list what improved. About also shows the version in use and the last check, with **Check design rules**. Developers who keep their own copy can turn this off: Settings > Design rules > *I manage the design rules myself*.

- **CAD Master.exe** with the owl icon, in the Start menu and (if you tick it) on the desktop: it starts SOLIDWORKS with the CAD Master panel open, or brings a running SOLIDWORKS to the front and opens the panel. The Start menu also has **Uninstall CAD Master**.

### Changed
- License: GNU GPL version 3 instead of MIT. The name CAD Master and the owl logo and icons are not covered by it (TRADEMARKS.md): changed versions must use their own name and icons.

### Fixed
- Python or Git installed while SOLIDWORKS was open stayed invisible to Claude until SOLIDWORKS restarted. Claude now always gets the current PATH.
- Claude now uses the Python that the setup check found, for example `py -3` on PCs where only the Python launcher works.
- Update checks work on PCs whose firewall blocks SOLIDWORKS from the internet (common on locked-down networks): CAD Master downloads through Windows' own `curl.exe`. SOLIDWORKS itself stays blocked.

### Planned
- Opt-in, anonymous sharing of lessons learned (see PRIVACY.md).

## 0.9.1 (2026-11-01)
### Added
- **Installer**: `CADMaster-Setup-0.9.1.exe` installs CAD Master for your Windows user, with one Windows administrator prompt so SOLIDWORKS lists it. It appears in *Apps & features* with an uninstaller. You can install while SOLIDWORKS is open; the new version loads at the next start. The previous version is kept for rolling back.
- Designer-friendly chat: one gears line says what Claude is doing ("Revolving the body · step 3 of 8"). It also shows a *hands off* tag, the latest model view and a timer. The technical process moved to a new **Details** tab.
- Drag and drop: drop pictures, sketches, STEP/IGES/STL/SOLIDWORKS files and PDFs onto the panel. Drag file chips and model views out. Large pictures are scaled down automatically.
- Bypass mode, with a one-time confirmation: Claude makes the design decisions and builds without asking. The plan card has an "Approve, Claude builds it all" button.
- Effort level **Ultracode**.
- `/cad:lessons`: turns a finished project's failures, diagnoses and successes into general rules for the solidworks-api skill.
- Claude now writes and reports like a mechanical design engineer: no code in the chat, design-term status lines, engineering judgement on process, fits and standards.

### Fixed
- SOLIDWORKS crashed about 12 s after start when CAD Master loaded at start-up. The panel now starts once SOLIDWORKS has finished starting.
- A possible SOLIDWORKS crash while scripts opened and closed documents: the add-in no longer reads the model inside SOLIDWORKS' document-change event, or while Claude is working.
- Build scripts could start a hidden second SOLIDWORKS that took over later launches. Inside CAD Master they now attach only to the SOLIDWORKS that hosts the panel.
- The add-in's log didn't work inside SOLIDWORKS. It now logs each start-up and shut-down step, any unhandled error, and has a fallback file.

## 0.9.0 (2026-10-31), first working build
- Claude Code in a SOLIDWORKS task pane: chat, tasks, terminal, history, account, settings.
- CAD Master ribbon tab and Tools menu with 17 commands.
- Local SOLIDWORKS tools for Claude: status, selection, feature tree, screenshot, checks, mass and custom properties, macros.
- Plan-first workflow with plan approval, permission and question cards.
