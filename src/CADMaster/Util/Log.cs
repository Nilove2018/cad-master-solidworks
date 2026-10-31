using System;
using System.IO;

namespace CADMaster
{
    public static class Log
    {
        static readonly object Gate = new object();

        public static string FilePath => Path.Combine(Paths.LogDir, "cadmaster-" + DateTime.Now.ToString("yyyyMMdd") + ".log");

        public static void Info(string message) => Write("INFO ", message);

        public static void Error(string message, Exception ex = null) => Write("ERROR", ex == null ? message : message + ": " + ex);

        static void Write(string level, string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Paths.LogDir);
                    File.AppendAllText(FilePath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + level + " " + message + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                // Never lose a line: fall back to %TEMP% and record why the normal log failed.
                try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "cadmaster-fallback.log"), DateTime.Now.ToString("HH:mm:ss.fff") + " " + level + " " + message + " [log failed: " + ex.Message + "]" + Environment.NewLine); } catch { }
            }
        }

        static bool hooked;

        /// <summary>Records .NET errors that nothing caught, so a SOLIDWORKS crash caused by the add-in leaves a trace.</summary>
        public static void HookUnhandled()
        {
            if (hooked) return;
            hooked = true;
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Error("UNHANDLED (terminating=" + e.IsTerminating + ")", e.ExceptionObject as Exception);
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) => { Error("unobserved task", e.Exception); e.SetObserved(); };
        }
    }
}
