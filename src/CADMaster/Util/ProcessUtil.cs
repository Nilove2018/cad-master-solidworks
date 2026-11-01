using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CADMaster
{
    public static class ProcessUtil
    {
        /// <summary>Quotes one argument by the Windows CommandLineToArgvW rules.</summary>
        public static string Quote(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return "\"\"";
            if (arg.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0) return arg;
            var sb = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\') { backslashes++; continue; }
                if (c == '"') { sb.Append('\\', backslashes * 2 + 1); sb.Append('"'); }
                else { sb.Append('\\', backslashes); sb.Append(c); }
                backslashes = 0;
            }
            sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }

        public static string Join(IEnumerable<string> args)
        {
            var sb = new StringBuilder();
            foreach (var a in args) { if (sb.Length > 0) sb.Append(' '); sb.Append(Quote(a)); }
            return sb.ToString();
        }

        /// <summary>Environment variables a nested Claude Code would misread as "already inside Claude Code".</summary>
        public static readonly string[] ScrubbedEnv = { "CLAUDECODE", "CLAUDE_CODE_ENTRYPOINT", "CLAUDE_CODE_SSE_PORT", "CLAUDE_CODE_SESSION_ID", "CLAUDE_AGENT_SDK_VERSION" };

        public static void ApplyEnv(System.Collections.Specialized.StringDictionary target, IDictionary<string, string> extra)
        {
            foreach (var k in ScrubbedEnv) target.Remove(k);
            target["PATH"] = FreshPath();
            if (extra != null) foreach (var kv in extra) target[kv.Key] = kv.Value;
        }

        /// <summary>
        /// PATH as Windows has it now (machine + user, from the registry) plus this process's own entries.
        /// SOLIDWORKS keeps the PATH it started with, so without this a program installed while it runs
        /// (Python, Git, Claude Code) stays invisible to Claude until SOLIDWORKS restarts.
        /// </summary>
        public static string FreshPath()
        {
            var parts = new List<string>();
            void Add(string list)
            {
                foreach (var p in (list ?? "").Split(';'))
                {
                    var d = Environment.ExpandEnvironmentVariables(p.Trim());
                    if (d.Length > 0 && !parts.Exists(x => string.Equals(x.TrimEnd('\\'), d.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))) parts.Add(d);
                }
            }
            string Reg(Microsoft.Win32.RegistryKey root, string key)
            {
                try
                {
                    using (var k = root.OpenSubKey(key))
                        return k?.GetValue("Path", "", Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                }
                catch { return null; }
            }
            Add(Reg(Microsoft.Win32.Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment"));
            Add(Reg(Microsoft.Win32.Registry.CurrentUser, "Environment"));
            Add(Environment.GetEnvironmentVariable("PATH"));
            return string.Join(";", parts);
        }

        public class Result
        {
            public int ExitCode; public string Stdout = ""; public string Stderr = "";
            public bool Ok => ExitCode == 0;
            /// <summary>The last lines of stdout and stderr, for showing what went wrong.</summary>
            public string Tail(int chars = 1500)
            {
                var s = (Stdout + "\n" + Stderr).Trim();
                return s.Length > chars ? "…" + s.Substring(s.Length - chars) : s;
            }
        }

        /// <summary>Runs a console program without a window and captures its output.</summary>
        public static Result Run(string exe, IEnumerable<string> args, string cwd = null, int timeoutMs = 30000, IDictionary<string, string> env = null)
        {
            var psi = new ProcessStartInfo(exe, Join(args))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false),
                WorkingDirectory = cwd ?? Paths.UserHome,
            };
            ApplyEnv(psi.EnvironmentVariables, env);
            using (var p = Process.Start(psi))
            {
                var outTask = p.StandardOutput.ReadToEndAsync();
                var errTask = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return new Result { ExitCode = -1, Stderr = "timed out" }; }
                Task.WaitAll(new Task[] { outTask, errTask }, 2000);
                return new Result { ExitCode = p.ExitCode, Stdout = outTask.Result, Stderr = errTask.Result };
            }
        }
    }

    /// <summary>Windows job object: every process put in it dies when the job is closed (SOLIDWORKS exits, Stop, new chat).</summary>
    public sealed class JobObject : IDisposable
    {
        IntPtr handle;

        public JobObject()
        {
            handle = CreateJobObject(IntPtr.Zero, null);
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            int size = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                SetInformationJobObject(handle, 9 /* JobObjectExtendedLimitInformation */, ptr, (uint)size);
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }

        public void Assign(IntPtr processHandle) { if (handle != IntPtr.Zero) AssignProcessToJobObject(handle, processHandle); }

        public void Terminate() { if (handle != IntPtr.Zero) TerminateJobObject(handle, 1); }

        public void Dispose()
        {
            if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IO_COUNTERS { public ulong a, b, c, d, e, f; }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attrs, string name);
        [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);
        [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll")] static extern bool TerminateJobObject(IntPtr job, uint exitCode);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    }
}
