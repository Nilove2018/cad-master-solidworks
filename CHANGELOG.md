# Changelog

Each version lists **what improved** in plain words. CAD Master will show these notes when an update is available.

## [Unreleased] (towards 1.0.0)
### Planned
- Automatic updates: an "update available" bar with notes for the app; silent updates for the design rules.
- First-run setup: Claude Code, sign-in, Python and pywin32, the skill and a SOLIDWORKS test, each with a fix button.
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
