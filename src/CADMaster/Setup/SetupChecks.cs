using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CADMaster
{
    /// <summary>The Python Claude's build scripts run with.</summary>
    public sealed class PythonInfo
    {
        /// <summary>sys.executable of the working interpreter.</summary>
        public string Exe;
        public string Version;
        /// <summary>What Claude types to run it: "python", "py -3" or a quoted full path.</summary>
        public string Command;
        public string Pywin32;          // pywin32 version, or null when it isn't installed
        public bool VersionOk => System.Version.TryParse(Version, out var v) && v >= new System.Version(3, 9);
    }

    /// <summary>Finds a working Python: Settings path, then `python` on PATH, then the py launcher.</summary>
    public static class PythonLocator
    {
        // Prints the version, sys.executable and the pywin32 version (or "-").
        const string Probe =
            "import sys\n" +
            "print('%d.%d.%d' % sys.version_info[:3])\n" +
            "print(sys.executable)\n" +
            "try:\n" +
            "    import pythoncom, win32com.client\n" +
            "    try:\n" +
            "        import importlib.metadata as md\n" +
            "        print(md.version('pywin32'))\n" +
            "    except Exception:\n" +
            "        print('installed')\n" +
            "except Exception:\n" +
            "    print('-')\n";

        public static PythonInfo Find(Settings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.PythonPath) && File.Exists(settings.PythonPath))
            {
                var p = Try(settings.PythonPath, new string[0], ProcessUtil.Quote(settings.PythonPath));
                if (p != null) return p;
            }
            var path = ProcessUtil.FreshPath().Split(';');
            // The first python.exe on PATH is what Claude gets when it types "python". The Windows Store
            // stub (WindowsApps\python.exe without the Store Python) fails the probe, so it is skipped.
            foreach (var dir in path)
            {
                var exe = SafeCombine(dir, "python.exe");
                if (exe == null || !File.Exists(exe)) continue;
                var p = Try(exe, new string[0], "python");
                if (p != null) return p;
                break;
            }
            foreach (var dir in path)
            {
                var exe = SafeCombine(dir, "py.exe");
                if (exe == null || !File.Exists(exe)) continue;
                var p = Try(exe, new[] { "-3" }, "py -3");
                if (p != null) return p;
                break;
            }
            return null;
        }

        static PythonInfo Try(string exe, string[] pre, string command)
        {
            try
            {
                var r = ProcessUtil.Run(exe, pre.Concat(new[] { "-c", Probe }), null, 20000);
                var lines = r.Stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (r.ExitCode != 0 || lines.Length < 3) return null;
                return new PythonInfo { Version = lines[0].Trim(), Exe = lines[1].Trim(), Command = command, Pywin32 = lines[2].Trim() == "-" ? null : lines[2].Trim() };
            }
            catch { return null; }
        }

        internal static string SafeCombine(string dir, string file)
        {
            try { return dir.Trim().Length == 0 ? null : Path.Combine(dir.Trim(), file); } catch { return null; }
        }
    }

    /// <summary>Finds the solidworks-api skill (the "design rules") and its examples\swlib.py.</summary>
    public static class SkillLocator
    {
        public const string Marketplace = "Nilove2018/solidworks-api-skill";
        public const string PluginId = "solidworks-api@solidworks-api-skill";

        /// <summary>
        /// The skill's examples\swlib.py: a developer checkout from Settings, else the newest copy Claude Code
        /// installed with the plugin (~/.claude/plugins), else a hand-copied skill folder.
        /// </summary>
        public static string FindSwlib(Settings settings)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(settings.SkillRepoPath))
                {
                    var dev = Path.Combine(settings.SkillRepoPath, "examples", "swlib.py");
                    if (File.Exists(dev)) return dev;
                }
                var plugins = Path.Combine(Paths.ClaudeConfigDir, "plugins");
                if (Directory.Exists(plugins))
                {
                    var newest = Directory.EnumerateFiles(plugins, "swlib.py", SearchOption.AllDirectories)
                        .Where(f => f.IndexOf("solidworks-api", StringComparison.OrdinalIgnoreCase) >= 0)
                        .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                    if (newest != null) return newest;
                }
                var skill = Path.Combine(Paths.ClaudeConfigDir, "skills", "solidworks-api", "examples", "swlib.py");
                if (File.Exists(skill)) return skill;
            }
            catch (Exception ex) { Log.Error("find swlib", ex); }
            return null;
        }

        /// <summary>Where the skill comes from ("plugin", "your own copy", ...) and its version, or null if missing.</summary>
        public static (string source, string version)? Find(Settings settings, string claudeExe)
        {
            if (claudeExe != null)
            {
                try
                {
                    var r = ProcessUtil.Run(claudeExe, new[] { "plugin", "list", "--json" }, null, 30000);
                    if (Json.Parse(r.Stdout) is object[] list)
                        foreach (var item in list.OfType<Dictionary<string, object>>())
                        {
                            var id = item.Str("id") ?? item.Str("name") ?? "";
                            if (id.StartsWith("solidworks-api", StringComparison.OrdinalIgnoreCase) && item.Bool("enabled", true))
                                return ("installed by CAD Master", item.Str("version"));
                        }
                }
                catch (Exception ex) { Log.Error("plugin list", ex); }
            }
            if (!string.IsNullOrWhiteSpace(settings.SkillRepoPath) && File.Exists(Path.Combine(settings.SkillRepoPath, "SKILL.md")))
                return ("developer copy", ManifestVersion(settings.SkillRepoPath));
            var own = Path.Combine(Paths.ClaudeConfigDir, "skills", "solidworks-api");
            if (File.Exists(Path.Combine(own, "SKILL.md")))
                return ("your own copy in ~/.claude/skills", ManifestVersion(own));
            return null;
        }

        static string ManifestVersion(string root)
        {
            try
            {
                var f = Path.Combine(root, ".claude-plugin", "plugin.json");
                return File.Exists(f) ? Json.ParseObject(File.ReadAllText(f)).Str("version") : null;
            }
            catch { return null; }
        }
    }

    /// <summary>Result of one setup check, as the panel shows it.</summary>
    public sealed class SetupItem
    {
        public string Id, Title, Detail, Fix, FixLabel;
        /// <summary>ok, missing, warn, optional, blocked (needs an earlier step), untested, fail.</summary>
        public string State;
        public bool Required;
        public Dictionary<string, object> ToJson() => Json.O("id", Id, "title", Title, "state", State, "required", Required, "detail", Detail, "fix", Fix, "fixLabel", FixLabel);
    }

    public sealed class SetupReport
    {
        public List<SetupItem> Items = new List<SetupItem>();
        public Dictionary<string, object> Auth;
        public PythonInfo Python;
        public (string source, string version)? Skill;
        public bool Ready => Items.Where(i => i.Required).All(i => i.State == "ok");
    }

    /// <summary>
    /// First-run setup: what CAD Master needs on this PC, each with a fix. The checks only read; the fixes
    /// install with the official installers (Claude Code's install script, winget, pip, Claude Code's plugin system).
    /// </summary>
    public static class SetupChecks
    {
        public static SetupReport Run(Settings settings, bool swConnected, Dictionary<string, object> swTest)
        {
            var rep = new SetupReport();
            var exe = ClaudeLocator.Find(settings);

            // 1. Claude Code
            string version = "";
            if (exe != null) try { version = ProcessUtil.Run(exe, new[] { "--version" }, null, 15000).Stdout.Trim(); } catch { }
            rep.Items.Add(exe == null
                ? new SetupItem { Id = "claude", Title = "Claude Code", Required = true, State = "missing", Fix = "install_claude", FixLabel = "Install Claude Code",
                                  Detail = "The engine behind CAD Master, from Anthropic. Installs for your Windows user, no administrator needed." }
                : new SetupItem { Id = "claude", Title = "Claude Code", Required = true, State = "ok", Detail = "Version " + version.Replace(" (Claude Code)", "") });

            // 2. Sign-in
            var status = new Dictionary<string, object>();
            if (exe != null)
            {
                try { status = Json.ParseObject(ProcessUtil.Run(exe, new[] { "auth", "status", "--json" }).Stdout) ?? status; }
                catch (Exception ex) { Log.Error("auth status", ex); }
            }
            bool loggedIn = status.Bool("loggedIn");
            rep.Auth = Json.O("installed", exe != null, "exe", exe, "version", version, "loggedIn", loggedIn, "status", status);
            var plan = status.Str("subscriptionType");
            rep.Items.Add(exe == null
                ? new SetupItem { Id = "signin", Title = "Your Claude account", Required = true, State = "blocked", Detail = "After Claude Code is installed." }
                : loggedIn
                    ? new SetupItem { Id = "signin", Title = "Your Claude account", Required = true, State = "ok",
                                      Detail = string.Join(" · ", new[] { status.Str("email"), string.IsNullOrEmpty(plan) ? status.Str("authMethod") : Cap(plan) + " plan" }.Where(s => !string.IsNullOrEmpty(s))) }
                    : new SetupItem { Id = "signin", Title = "Your Claude account", Required = true, State = "missing", Fix = "sign_in", FixLabel = "Sign in",
                                      Detail = "Sign in with your own Claude Pro, Max, Team or Enterprise account, or an Anthropic Console account." });

            // 3. Python and 4. pywin32
            var py = rep.Python = PythonLocator.Find(settings);
            if (py == null)
                rep.Items.Add(new SetupItem { Id = "python", Title = "Python", Required = true, State = "missing", Fix = "install_python", FixLabel = "Install Python",
                                              Detail = "Claude writes its SOLIDWORKS build scripts in Python. Installs Python 3.12 for your Windows user." });
            else if (!py.VersionOk)
                rep.Items.Add(new SetupItem { Id = "python", Title = "Python", Required = true, State = "missing", Fix = "install_python", FixLabel = "Install Python 3.12",
                                              Detail = "Python " + py.Version + " is too old; CAD Master needs 3.9 or newer." });
            else
                rep.Items.Add(new SetupItem { Id = "python", Title = "Python", Required = true, State = "ok", Detail = "Python " + py.Version });

            rep.Items.Add(py == null || !py.VersionOk
                ? new SetupItem { Id = "pywin32", Title = "SOLIDWORKS link for Python (pywin32)", Required = true, State = "blocked", Detail = "After Python is installed." }
                : py.Pywin32 == null
                    ? new SetupItem { Id = "pywin32", Title = "SOLIDWORKS link for Python (pywin32)", Required = true, State = "missing", Fix = "install_pywin32", FixLabel = "Install pywin32",
                                      Detail = "Lets Python scripts drive SOLIDWORKS through its API." }
                    : new SetupItem { Id = "pywin32", Title = "SOLIDWORKS link for Python (pywin32)", Required = true, State = "ok", Detail = "pywin32 " + py.Pywin32 });

            // 5. Git for Windows: Claude Code downloads and updates plugins (the design rules) with git
            var bash = FindGitBash();
            rep.Items.Add(bash != null
                ? new SetupItem { Id = "git", Title = "Git for Windows", Required = true, State = "ok", Detail = "Found. Claude can use Bash as well as PowerShell." }
                : new SetupItem { Id = "git", Title = "Git for Windows", Required = true, State = "missing", Fix = "install_git", FixLabel = "Install Git",
                                  Detail = "Claude Code downloads and updates the design rules with it. Windows may ask for administrator approval." });

            // 6. Design rules (solidworks-api skill)
            var skill = rep.Skill = SkillLocator.Find(settings, exe);
            var skillBlocked = exe == null ? "After Claude Code is installed." : bash == null ? "After Git for Windows is installed." : null;
            rep.Items.Add(skill != null
                ? new SetupItem { Id = "skill", Title = "Design rules (solidworks-api skill)", Required = true, State = "ok",
                                  Detail = (skill.Value.version != null ? "Version " + skill.Value.version + ", " : "") + skill.Value.source }
                : skillBlocked != null
                    ? new SetupItem { Id = "skill", Title = "Design rules (solidworks-api skill)", Required = true, State = "blocked", Detail = skillBlocked }
                    : new SetupItem { Id = "skill", Title = "Design rules (solidworks-api skill)", Required = true, State = "missing", Fix = "install_skill", FixLabel = "Install design rules",
                                      Detail = "How Claude builds correct SOLIDWORKS models: API rules, checks and lessons from real projects. Updates itself." });

            // 7. The SOLIDWORKS test
            if (!swConnected)
                rep.Items.Add(new SetupItem { Id = "swtest", Title = "SOLIDWORKS test", State = "blocked", Detail = "Runs when CAD Master is open inside SOLIDWORKS." });
            else if (swTest == null)
                rep.Items.Add(new SetupItem { Id = "swtest", Title = "SOLIDWORKS test", State = "untested", Fix = "run_sw_test", FixLabel = "Run test",
                                              Detail = "Builds a 40 × 20 × 10 mm test block in a new part, checks it and closes it without saving. About 20 seconds." });
            else if (swTest.Bool("ok"))
                rep.Items.Add(new SetupItem { Id = "swtest", Title = "SOLIDWORKS test", State = "ok", Fix = "run_sw_test", FixLabel = "Run again",
                                              Detail = "Passed: built and checked the test block in " + swTest.Dbl("seconds").ToString("0") + " s." });
            else
                rep.Items.Add(new SetupItem { Id = "swtest", Title = "SOLIDWORKS test", State = "fail", Fix = "run_sw_test", FixLabel = "Run again",
                                              Detail = "Failed: " + (swTest.Str("error") ?? "no result") });
            return rep;
        }

        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        /// <summary>Git Bash, the way Claude Code finds it: CLAUDE_CODE_GIT_BASH_PATH, then git.exe on PATH.</summary>
        public static string FindGitBash()
        {
            var set = Environment.GetEnvironmentVariable("CLAUDE_CODE_GIT_BASH_PATH");
            if (!string.IsNullOrEmpty(set) && File.Exists(set)) return set;
            foreach (var dir in ProcessUtil.FreshPath().Split(';'))
            {
                var git = PythonLocator.SafeCombine(dir, "git.exe");
                if (git == null || !File.Exists(git)) continue;
                var root = Path.GetDirectoryName(Path.GetDirectoryName(git)); // <Git>\cmd\git.exe or <Git>\bin\git.exe
                var bash = Path.Combine(root, "bin", "bash.exe");
                if (File.Exists(bash)) return bash;
            }
            var std = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe");
            return File.Exists(std) ? std : null;
        }

        /// <summary>Finds an executable on the current PATH (e.g. winget.exe).</summary>
        public static string Which(string exe)
        {
            foreach (var dir in ProcessUtil.FreshPath().Split(';'))
            {
                var p = PythonLocator.SafeCombine(dir, exe);
                if (p != null && File.Exists(p)) return p;
            }
            return null;
        }
    }

    /// <summary>Result of a setup fix.</summary>
    public sealed class FixResult
    {
        public bool Ok;
        public string Message, Log, Url;
    }

    /// <summary>The fixes behind the setup buttons. Each one runs hidden and may take minutes.</summary>
    public static class SetupFixes
    {
        const int Minutes = 60000;
        static readonly string[] WingetAccept = { "--silent", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity" };

        public static FixResult InstallClaude()
        {
            // The copy inside the Claude desktop app can install the standalone one; otherwise the official script.
            var bundled = ClaudeLocator.BundledCopy();
            var r = bundled != null
                ? ProcessUtil.Run(bundled, new[] { "install" }, null, 10 * Minutes)
                : ProcessUtil.Run(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                    new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", "irm https://claude.ai/install.ps1 | iex" }, null, 10 * Minutes);
            var found = File.Exists(Path.Combine(Paths.UserHome, ".local", "bin", "claude.exe"));
            return new FixResult { Ok = found, Log = r.Tail(), Message = found ? "Claude Code is installed" : "Claude Code didn't install", Url = found ? null : "https://code.claude.com/docs/en/setup" };
        }

        public static FixResult InstallPython() => Winget("Python.Python.3.12", "Python", "https://www.python.org/downloads/windows/", "--scope", "user");

        public static FixResult InstallGit() => Winget("Git.Git", "Git for Windows", "https://git-scm.com/downloads/win");

        static FixResult Winget(string id, string name, string url, params string[] extra)
        {
            var winget = SetupChecks.Which("winget.exe");
            if (winget == null)
                return new FixResult { Message = "Windows Package Manager (winget) isn't available. Opening the " + name + " download page instead.", Url = url };
            var r = ProcessUtil.Run(winget, new[] { "install", "-e", "--id", id }.Concat(extra).Concat(WingetAccept), null, 15 * Minutes);
            // 0x8A15002B: already installed and no newer version
            bool ok = r.Ok || r.Stdout.IndexOf("already installed", StringComparison.OrdinalIgnoreCase) >= 0;
            return new FixResult { Ok = ok, Log = r.Tail(), Message = ok ? name + " is installed" : name + " didn't install (winget exit code " + r.ExitCode + ")", Url = ok ? null : url };
        }

        public static FixResult InstallPywin32(PythonInfo py)
        {
            if (py == null) return new FixResult { Message = "Install Python first." };
            var r = ProcessUtil.Run(py.Exe, new[] { "-m", "pip", "install", "--user", "--upgrade", "--disable-pip-version-check", "pywin32" }, null, 10 * Minutes);
            return new FixResult { Ok = r.Ok, Log = r.Tail(), Message = r.Ok ? "pywin32 is installed" : "pip couldn't install pywin32" };
        }

        public static FixResult InstallSkill(string claudeExe)
        {
            if (claudeExe == null) return new FixResult { Message = "Install Claude Code first." };
            var add = ProcessUtil.Run(claudeExe, new[] { "plugin", "marketplace", "add", SkillLocator.Marketplace }, null, 5 * Minutes);
            var install = ProcessUtil.Run(claudeExe, new[] { "plugin", "install", SkillLocator.PluginId, "--scope", "user" }, null, 5 * Minutes);
            return new FixResult { Ok = install.Ok, Log = (add.Tail(600) + "\n" + install.Tail(900)).Trim(),
                                   Message = install.Ok ? "Design rules installed" : "The design rules didn't install" };
        }

        /// <summary>Builds and checks a test block through Python, pywin32, swlib and this SOLIDWORKS (python\cadmaster_selftest.py).</summary>
        public static Dictionary<string, object> SolidWorksTest(PythonInfo py, string swlib, IDictionary<string, string> env)
        {
            if (py == null || py.Pywin32 == null) return Json.O("ok", false, "error", "Python and pywin32 are needed first.");
            if (swlib == null) return Json.O("ok", false, "error", "Install the design rules first.");
            var script = Path.Combine(Paths.PythonDir, "cadmaster_selftest.py");
            Directory.CreateDirectory(Paths.RuntimeDir);
            var r = ProcessUtil.Run(py.Exe, new[] { script, Path.GetDirectoryName(swlib) }, Paths.RuntimeDir, 3 * Minutes, env);
            var last = r.Stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            var result = last == null ? null : Json.ParseObject(last);
            if (result == null) result = Json.O("ok", false, "error", r.ExitCode == -1 ? "SOLIDWORKS didn't answer within 3 minutes. Is a dialog open?" : "the test script failed: " + r.Tail(400));
            return result;
        }
    }
}
