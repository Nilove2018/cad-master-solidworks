# Privacy

CAD Master runs on your PC. This page says exactly what leaves it.

## Today (pre-release)
| Data | Where it goes |
|---|---|
| Your chat, attachments and what Claude reads from your model (screenshots, feature tree, properties) | To **Anthropic**, through Claude Code, under **your own** Claude account and Anthropic's terms. This is the same as using Claude Code in a terminal |
| Settings, logs, chat history | Stay on your PC: `%LOCALAPPDATA%\CADMaster` and `%USERPROFILE%\.claude` |
| SOLIDWORKS tool server | Listens on `localhost` only, with a random token for each session; not reachable from the network |
| Your password or Claude tokens | CAD Master never sees them. Sign-in is done by Claude Code (`claude auth login`) |

CAD Master itself sends nothing to its developers or anyone else.

## Planned for v1.0
### Update checks
Once a day CAD Master will ask GitHub (`api.github.com`) whether a newer CAD Master or design-rule version exists. GitHub sees your IP address, as with any web request. Nothing else is sent. You can turn it off in Settings.

### Sharing lessons (opt-in, off by default)
You can choose to help improve the design rules. If you turn it on, once a day Claude reviews your recent chats on your PC and writes **general** failure and success records. For example: "when a screw mate moves the wrong way, measure the travel per turn before changing the mate".

| Sent, only if you opt in | Never sent |
|---|---|
| General rules: symptom, root cause, fix, the check that found it, techniques that worked | Your models, drawings, STEP or any other file |
| Version numbers of SOLIDWORKS, CAD Master and the skill | Part names, dimensions, file paths, screenshots |
| A random install ID, to block spam and duplicates (you can reset it) | Your chats or prompts, your name, email, Claude account or company |

- Before anything is sent, a filter on your PC removes paths, file names, email addresses, links and part-number-like codes.
- You choose **Ask me before each send** or **Send automatically**, and you can view everything that was sent.
- The records go through a small receiving service, which does not store your IP address, to the maintainer for review. Accepted rules are published openly in the solidworks-api skill.

This page will be updated before any of this ships.

## Contact
Open an issue in this repository for privacy questions.
