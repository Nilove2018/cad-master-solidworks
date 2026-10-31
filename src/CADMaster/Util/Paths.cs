using System;
using System.IO;

namespace CADMaster
{
    public static class Paths
    {
        /// <summary>Folder holding CADMaster.dll and its ui, plugin, icons and python folders.</summary>
        public static string AddinDir => Path.GetDirectoryName(typeof(Paths).Assembly.Location);

        public static string UiDir => Path.Combine(AddinDir, "ui");
        public static string PluginDir => Path.Combine(AddinDir, "plugin", "cad");
        public static string IconsDir => Path.Combine(AddinDir, "icons");
        public static string PythonDir => Path.Combine(AddinDir, "python");

        /// <summary>%LOCALAPPDATA%\CADMaster: settings, logs, WebView2 profile, per-session MCP config.</summary>
        public static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CADMaster");
        public static string LogDir => Path.Combine(DataDir, "logs");
        public static string WebViewDir => Path.Combine(DataDir, "WebView2");
        public static string RuntimeDir => Path.Combine(DataDir, "runtime");

        public static string UserHome => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        public static string ClaudeConfigDir => Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? Path.Combine(UserHome, ".claude");

        public static string DefaultWorkspace => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CAD Master");
    }
}
