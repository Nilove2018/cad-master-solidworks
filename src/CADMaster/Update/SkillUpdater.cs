using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CADMaster
{
    /// <summary>
    /// Keeps the design rules (the solidworks-api plugin) up to date through Claude Code's own plugin
    /// system: refresh the marketplace, then update the plugin. Terminal users get the same updates the
    /// same way. Only touches a copy Claude Code installed as a plugin; a hand-copied skill or a
    /// developer checkout is left alone.
    /// </summary>
    public static class SkillUpdater
    {
        /// <summary>The marketplace's name in .claude-plugin/marketplace.json (the part after @ in the plugin id).</summary>
        public const string MarketplaceName = "solidworks-api-skill";

        public sealed class Result
        {
            /// <summary>updated, up_to_date, not_installed (no plugin copy to update), failed.</summary>
            public string Outcome, OldVersion, NewVersion, Notes, Message, Log;
        }

        public static Result Run(string claudeExe)
        {
            var installed = Installed(claudeExe);
            if (installed == null) return new Result { Outcome = "not_installed" };

            var refresh = ProcessUtil.Run(claudeExe, new[] { "plugin", "marketplace", "update", MarketplaceName, "--json" }, null, 3 * 60000);
            if (!refresh.Ok) return Failed("The design rules' source couldn't be refreshed", refresh);

            // No -y: if the plugin's source ever declares an install command, Claude Code asks a person
            // to confirm it, and this silent update fails instead of accepting it.
            var update = ProcessUtil.Run(claudeExe, new[] { "plugin", "update", SkillLocator.PluginId, "--json", "--scope", installed.Str("scope", "user") }, null, 5 * 60000);
            var r = LastJson(update.Stdout);
            if (!update.Ok || r == null || r.Str("outcome") != "ok") return Failed(r?.Str("message") ?? "The design rules didn't update", update);

            var res = new Result { OldVersion = r.Str("oldVersion") ?? installed.Str("version"), NewVersion = r.Str("newVersion"), Message = r.Str("message") };
            res.Outcome = r.Str("updateOutcome") == "up_to_date" || res.OldVersion == res.NewVersion ? "up_to_date" : "updated";
            if (res.Outcome == "updated")
            {
                var path = Installed(claudeExe)?.Str("installPath");
                res.Notes = path == null ? null : ChangelogBetween(Path.Combine(path, "CHANGELOG.md"), res.OldVersion, res.NewVersion);
            }
            return res;
        }

        /// <summary>The plugin's entry in `claude plugin list --json` (version, scope, installPath), or null.</summary>
        static Dictionary<string, object> Installed(string claudeExe)
        {
            var list = ProcessUtil.Run(claudeExe, new[] { "plugin", "list", "--json" }, null, 30000);
            return (Json.Parse(list.Stdout) as object[] ?? new object[0]).OfType<Dictionary<string, object>>()
                .FirstOrDefault(p => p.Str("id") == SkillLocator.PluginId);
        }

        static Result Failed(string message, ProcessUtil.Result r) => new Result { Outcome = "failed", Message = message, Log = r.Tail() };

        static Dictionary<string, object> LastJson(string stdout) =>
            (stdout ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("{")).Select(Json.ParseObject).LastOrDefault(o => o != null);

        static readonly Regex VersionHeading = new Regex(@"^## \[?(\d+\.\d+\.\d+(?:-[0-9A-Za-z.]+)?)\]?(.*)$");

        /// <summary>The CHANGELOG sections newer than oldVersion, up to newVersion: what improved.</summary>
        public static string ChangelogBetween(string changelog, string oldVersion, string newVersion)
        {
            try
            {
                if (!File.Exists(changelog)) return null;
                var sb = new StringBuilder();
                bool take = false;
                foreach (var line in File.ReadAllLines(changelog, Encoding.UTF8))
                {
                    var m = VersionHeading.Match(line);
                    if (m.Success)
                    {
                        var v = m.Groups[1].Value;
                        take = Updater.Compare(v, oldVersion) > 0 && Updater.Compare(v, newVersion) <= 0;
                        if (take) sb.AppendLine("#### " + v + m.Groups[2].Value.TrimEnd());
                        continue;
                    }
                    if (line.StartsWith("## ")) { take = false; continue; } // [Unreleased] and other sections
                    if (take) sb.AppendLine(line.StartsWith("### ") ? "##### " + line.Substring(4) : line);
                }
                var text = sb.ToString().Trim();
                return text.Length == 0 ? null : text;
            }
            catch (Exception ex) { Log.Error("skill changelog", ex); return null; }
        }
    }
}
