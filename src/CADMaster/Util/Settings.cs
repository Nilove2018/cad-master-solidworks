using System;
using System.IO;

namespace CADMaster
{
    /// <summary>User settings, stored as JSON in %LOCALAPPDATA%\CADMaster\settings.json.</summary>
    public class Settings
    {
        public string ClaudePath { get; set; } = "";
        /// <summary>"activeDoc": folder of the active document (if saved), else WorkspaceFolder. "fixed": always WorkspaceFolder.</summary>
        public string WorkspaceMode { get; set; } = "activeDoc";
        public string WorkspaceFolder { get; set; } = Paths.DefaultWorkspace;
        public string DefaultModel { get; set; } = "default";
        public string DefaultEffort { get; set; } = "";
        public string DefaultMode { get; set; } = "default";
        public bool PlanFirst { get; set; } = true;
        public bool AutoAttachContext { get; set; } = true;
        public string Theme { get; set; } = "dark";
        public int FontSize { get; set; } = 13;
        public string ExtraInstructions { get; set; } = "";
        public bool AllowBypass { get; set; } = false;
        public bool ShowThinking { get; set; } = true;
        /// <summary>Developers: a solidworks-api skill checkout whose examples\swlib.py should be used. Empty: find the installed skill.</summary>
        public string SkillRepoPath { get; set; } = "";
        /// <summary>python.exe Claude should use. Empty: found automatically (python on PATH, then the py launcher).</summary>
        public string PythonPath { get; set; } = "";
        /// <summary>The user finished first-run setup; afterwards it only shows when something required is missing.</summary>
        public bool SetupDone { get; set; } = false;
        /// <summary>Look for a new CAD Master version once a day.</summary>
        public bool AutoUpdateCheck { get; set; } = true;
        /// <summary>Also offer test versions (always on while the running version is one).</summary>
        public bool IncludePrereleases { get; set; } = false;
        /// <summary>When the last automatic check ran (ISO 8601).</summary>
        public string LastUpdateCheck { get; set; } = "";
        /// <summary>A version the user rolled back from: automatic checks don't offer it again.</summary>
        public string SkippedVersion { get; set; } = "";
        /// <summary>Developer mode: CAD Master never updates the design rules (the solidworks-api skill).</summary>
        public bool ManageSkillMyself { get; set; } = false;
        /// <summary>When the design rules were last checked for an update (ISO 8601).</summary>
        public string LastSkillCheck { get; set; } = "";
        /// <summary>The version that ran last time, to say "Updated to …" once after an update.</summary>
        public string LastRunVersion { get; set; } = "";

        static string FilePath => Path.Combine(Paths.DataDir, "settings.json");

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath)) return Json.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
            }
            catch (Exception ex) { Log.Error("settings load", ex); }
            return new Settings();
        }

        public void Save()
        {
            Directory.CreateDirectory(Paths.DataDir);
            File.WriteAllText(FilePath, Json.Serialize(this));
        }
    }
}
