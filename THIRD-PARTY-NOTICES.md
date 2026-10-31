# Third-party notices

CAD Master includes or uses the following components. Each is used under its own license.

## Bundled in the panel (`ui/vendor`, `ui/js/icons.js`)
| Component | Version | License | Source |
|---|---|---|---|
| xterm.js (`@xterm/xterm`) | 6.0.0 | MIT | https://github.com/xtermjs/xterm.js |
| xterm.js fit add-on (`@xterm/addon-fit`) | 0.11.0 | MIT | https://github.com/xtermjs/xterm.js |
| marked | 18.1.1 | MIT | https://github.com/markedjs/marked |
| DOMPurify | 3.4.17 | Apache-2.0 or MPL-2.0 (dual) | https://github.com/cure53/DOMPurify |
| Tabler Icons (subset, inlined) | 3.19.0 | MIT | https://github.com/tabler/tabler-icons |

## Downloaded at build time (not stored in this repo)
| Component | Version | License | Source |
|---|---|---|---|
| Microsoft Edge WebView2 SDK (`Microsoft.Web.WebView2`) | 1.0.4258.31 | Microsoft WebView2 SDK license (redistributable) | https://www.nuget.org/packages/Microsoft.Web.WebView2 |
| Roslyn C# compiler (`Microsoft.Net.Compilers.Toolset`), build tool only | 5.9.0 | MIT | https://github.com/dotnet/roslyn |

## Taken from your SOLIDWORKS installation
| Component | License |
|---|---|
| `SolidWorks.Interop.sldworks.dll`, `SolidWorks.Interop.swconst.dll`, `SolidWorks.Interop.swpublished.dll` (from `<SOLIDWORKS>\api\redist`) | © Dassault Systèmes SolidWorks Corporation. They are provided with SOLIDWORKS for building and redistributing add-ins, and are not stored in this repository |

## Used at run time, installed separately
- **Claude Code** by Anthropic, under Anthropic's terms: https://www.anthropic.com/legal
- **Microsoft Edge WebView2 Runtime**
- **Python** and **pywin32**, for Claude's build scripts
- The **solidworks-api skill** (MIT): https://github.com/Nilove2018/solidworks-api-skill

The full license texts are at the source links above.
