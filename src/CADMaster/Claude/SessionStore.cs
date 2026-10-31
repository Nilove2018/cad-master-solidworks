using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CADMaster
{
    /// <summary>Reads Claude Code's saved conversations (~/.claude/projects/&lt;encoded cwd&gt;/&lt;session&gt;.jsonl).</summary>
    public static class SessionStore
    {
        public static string ProjectDir(string cwd) =>
            Path.Combine(Paths.ClaudeConfigDir, "projects", Regex.Replace(cwd ?? "", "[^A-Za-z0-9]", "-"));

        public static List<Dictionary<string, object>> List(string cwd, int max = 40)
        {
            var result = new List<Dictionary<string, object>>();
            var dir = ProjectDir(cwd);
            if (!Directory.Exists(dir)) return result;
            foreach (var f in new DirectoryInfo(dir).GetFiles("*.jsonl").OrderByDescending(f => f.LastWriteTimeUtc).Take(max))
            {
                try
                {
                    string title = null, custom = null;
                    int turns = 0;
                    foreach (var line in ReadLines(f.FullName).Take(400))
                    {
                        var m = Json.ParseObject(line);
                        if (m == null) continue;
                        var type = m.Str("type");
                        if (type == "custom-title") custom = m.Str("customTitle") ?? custom;
                        else if (type == "summary" && title == null) title = m.Str("summary");
                        else if (type == "user" && !m.Bool("isMeta") && !m.Bool("isSidechain"))
                        {
                            var text = UserText(m.Obj("message"));
                            if (text == null) continue;
                            turns++;
                            if (title == null && !text.StartsWith("<")) title = text;
                        }
                    }
                    if (turns == 0 && custom == null) continue;
                    title = (custom ?? title ?? "(untitled)").Replace('\n', ' ').Trim();
                    if (title.Length > 90) title = title.Substring(0, 90) + "…";
                    result.Add(Json.O("id", Path.GetFileNameWithoutExtension(f.Name), "title", title,
                        "modified", f.LastWriteTime.ToString("yyyy-MM-dd HH:mm"), "turns", turns));
                }
                catch (Exception ex) { Log.Error("session list " + f.Name, ex); }
            }
            return result;
        }

        /// <summary>Raw JSON lines of the visible user/assistant messages of one session, for replay in the chat.</summary>
        public static List<string> Load(string cwd, string sessionId, int maxMessages = 400)
        {
            var path = Path.Combine(ProjectDir(cwd), sessionId + ".jsonl");
            var lines = new List<string>();
            if (!File.Exists(path)) return lines;
            foreach (var line in ReadLines(path))
            {
                var m = Json.ParseObject(line);
                if (m == null || m.Bool("isSidechain") || m.Bool("isMeta")) continue;
                var type = m.Str("type");
                if (type == "user" || type == "assistant") lines.Add(line);
            }
            return lines.Count > maxMessages ? lines.Skip(lines.Count - maxMessages).ToList() : lines;
        }

        static string UserText(Dictionary<string, object> message)
        {
            if (message == null) return null;
            if (message.TryGetValue("content", out var c))
            {
                if (c is string s) return s;
                if (c is object[] blocks)
                    foreach (var b in blocks)
                        if (b is Dictionary<string, object> d && d.Str("type") == "text") return d.Str("text");
            }
            return null;
        }

        static IEnumerable<string> ReadLines(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var r = new StreamReader(fs))
            {
                string line;
                while ((line = r.ReadLine()) != null) yield return line;
            }
        }
    }
}
