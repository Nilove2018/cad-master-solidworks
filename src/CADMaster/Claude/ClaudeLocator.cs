using System;
using System.IO;
using System.Linq;

namespace CADMaster
{
    /// <summary>Finds the Claude Code executable: Settings path, standalone install, PATH, then the copy bundled with the desktop app.</summary>
    public static class ClaudeLocator
    {
        public static string Find(Settings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.ClaudePath) && File.Exists(settings.ClaudePath)) return settings.ClaudePath;

            var standalone = Path.Combine(Paths.UserHome, ".local", "bin", "claude.exe");
            if (File.Exists(standalone)) return standalone;

            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                try
                {
                    var p = Path.Combine(dir.Trim(), "claude.exe");
                    if (dir.Trim().Length > 0 && File.Exists(p)) return p;
                }
                catch { }
            }
            return BundledCopy();
        }

        /// <summary>Newest claude.exe shipped inside the Claude desktop app (its folder changes on every update).</summary>
        public static string BundledCopy()
        {
            try
            {
                var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Claude-Data");
                if (!Directory.Exists(root)) return null;
                return Directory.GetDirectories(root, "claude_*")
                    .Select(d => Path.Combine(d, "claude-code"))
                    .Where(Directory.Exists)
                    .SelectMany(Directory.GetDirectories)
                    .Select(v => new { Dir = v, Ver = ParseVersion(Path.GetFileName(v)) })
                    .OrderByDescending(v => v.Ver)
                    .SelectMany(v => Directory.GetFiles(v.Dir, "claude.exe", SearchOption.AllDirectories))
                    .FirstOrDefault();
            }
            catch { return null; }
        }

        static Version ParseVersion(string s) => Version.TryParse(s, out var v) ? v : new Version(0, 0);
    }
}
