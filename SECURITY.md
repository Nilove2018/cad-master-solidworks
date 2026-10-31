# Security

## Reporting a vulnerability
Please **don't** open a public issue for security problems. Use GitHub's private reporting: *Security > Report a vulnerability* in this repository. Expect an answer within a week.

Relevant areas:
- the local MCP tool server: it must stay reachable only from `localhost` and only with the per-session token
- the WebView2 panel: untrusted content must not reach the host bridge; links must open in the browser, not in the panel
- updates and installers, once released: downloads must be verified (SHA-256) before use
- the planned lesson sharing: nothing beyond what [PRIVACY.md](PRIVACY.md) lists may leave the PC

## What CAD Master can do on your PC
CAD Master runs Claude Code under your account, and Claude can run commands and scripts on your PC to drive SOLIDWORKS:
- In **Plan**, **Ask** and **Accept edits** modes, it asks before running them.
- In **Bypass** mode, it doesn't ask.

Use Bypass only for work you're comfortable letting Claude carry out unattended, and keep backups of important files.
