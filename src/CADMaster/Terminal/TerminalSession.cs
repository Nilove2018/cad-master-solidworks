using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace CADMaster
{
    /// <summary>
    /// A Windows pseudo console (ConPTY) running one program, e.g. the interactive `claude`.
    /// Output goes to xterm.js in the panel; keystrokes come back through Write().
    /// </summary>
    public sealed class TerminalSession : IDisposable
    {
        IntPtr pseudoConsole, processHandle, threadHandle, attributeList;
        SafeFileHandle inputWrite, outputRead;
        FileStream input;
        JobObject job;
        volatile bool disposed;

        public event Action<string> Output;
        public event Action<int> Exited;

        public void Start(string commandLine, string cwd, int cols, int rows, IDictionary<string, string> extraEnv)
        {
            if (!CreatePipe(out var inputRead, out inputWrite, IntPtr.Zero, 0) ||
                !CreatePipe(out outputRead, out var outputWrite, IntPtr.Zero, 0))
                throw new InvalidOperationException("CreatePipe failed");

            int hr = CreatePseudoConsole(new COORD { X = (short)cols, Y = (short)rows }, inputRead, outputWrite, 0, out pseudoConsole);
            if (hr != 0) throw new InvalidOperationException("CreatePseudoConsole failed: 0x" + hr.ToString("X"));

            var si = new STARTUPINFOEX();
            si.StartupInfo.cb = Marshal.SizeOf(typeof(STARTUPINFOEX));
            IntPtr size = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            attributeList = Marshal.AllocHGlobal(size);
            if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref size)) throw new InvalidOperationException("InitializeProcThreadAttributeList failed");
            if (!UpdateProcThreadAttribute(attributeList, 0, (IntPtr)0x00020016 /* PSEUDOCONSOLE */, pseudoConsole, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new InvalidOperationException("UpdateProcThreadAttribute failed");
            si.lpAttributeList = attributeList;

            IntPtr env = BuildEnvironment(extraEnv);
            try
            {
                const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000, CREATE_UNICODE_ENVIRONMENT = 0x00000400;
                if (!CreateProcess(null, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false,
                        EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT, env, cwd, ref si, out var pi))
                    throw new InvalidOperationException("CreateProcess failed: " + Marshal.GetLastWin32Error());
                processHandle = pi.hProcess;
                threadHandle = pi.hThread;
            }
            finally { Marshal.FreeHGlobal(env); }

            inputRead.Dispose();
            outputWrite.Dispose();
            job = new JobObject();
            job.Assign(processHandle);
            input = new FileStream(inputWrite, FileAccess.Write);
            new Thread(ReadLoop) { IsBackground = true, Name = "conpty-out" }.Start();
            new Thread(WaitLoop) { IsBackground = true, Name = "conpty-wait" }.Start();
        }

        void ReadLoop()
        {
            var decoder = new UTF8Encoding(false).GetDecoder();
            var buffer = new byte[16384];
            var chars = new char[16384 + 4];
            try
            {
                using (var stream = new FileStream(outputRead, FileAccess.Read))
                {
                    int n;
                    while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        int c = decoder.GetChars(buffer, 0, n, chars, 0);
                        if (c > 0) Output?.Invoke(new string(chars, 0, c));
                    }
                }
            }
            catch (Exception ex) { if (!disposed) Log.Error("conpty read", ex); }
        }

        void WaitLoop()
        {
            WaitForSingleObject(processHandle, 0xFFFFFFFF);
            GetExitCodeProcess(processHandle, out uint code);
            if (!disposed) Exited?.Invoke((int)code);
        }

        public void Write(string data)
        {
            if (input == null || disposed) return;
            var bytes = Encoding.UTF8.GetBytes(data);
            try { input.Write(bytes, 0, bytes.Length); input.Flush(); } catch (Exception ex) { Log.Error("conpty write", ex); }
        }

        public void Resize(int cols, int rows)
        {
            if (pseudoConsole != IntPtr.Zero && cols > 0 && rows > 0)
                ResizePseudoConsole(pseudoConsole, new COORD { X = (short)cols, Y = (short)rows });
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { job?.Terminate(); job?.Dispose(); } catch { }
            var pc = pseudoConsole;
            pseudoConsole = IntPtr.Zero;
            // ClosePseudoConsole can block until its output is drained: run it off the UI thread.
            if (pc != IntPtr.Zero) ThreadPool.QueueUserWorkItem(_ => { try { ClosePseudoConsole(pc); } catch { } });
            try { input?.Dispose(); } catch { }
            if (processHandle != IntPtr.Zero) CloseHandle(processHandle);
            if (threadHandle != IntPtr.Zero) CloseHandle(threadHandle);
            if (attributeList != IntPtr.Zero) { DeleteProcThreadAttributeList(attributeList); Marshal.FreeHGlobal(attributeList); }
        }

        static IntPtr BuildEnvironment(IDictionary<string, string> extra)
        {
            var env = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry e in Environment.GetEnvironmentVariables()) env[(string)e.Key] = (string)e.Value;
            foreach (var k in ProcessUtil.ScrubbedEnv) env.Remove(k);
            env["PATH"] = ProcessUtil.FreshPath();
            env["TERM"] = "xterm-256color";
            env["COLORTERM"] = "truecolor";
            if (extra != null) foreach (var kv in extra) env[kv.Key] = kv.Value;
            var sb = new StringBuilder();
            foreach (var kv in env) sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\0');
            sb.Append('\0');
            return Marshal.StringToHGlobalUni(sb.ToString());
        }

        [StructLayout(LayoutKind.Sequential)] struct COORD { public short X, Y; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct STARTUPINFO
        {
            public int cb;
            public string lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)] struct STARTUPINFOEX { public STARTUPINFO StartupInfo; public IntPtr lpAttributeList; }

        [StructLayout(LayoutKind.Sequential)] struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

        [DllImport("kernel32.dll", SetLastError = true)] static extern int CreatePseudoConsole(COORD size, SafeFileHandle hInput, SafeFileHandle hOutput, uint flags, out IntPtr phPC);
        [DllImport("kernel32.dll", SetLastError = true)] static extern int ResizePseudoConsole(IntPtr hPC, COORD size);
        [DllImport("kernel32.dll", SetLastError = true)] static extern void ClosePseudoConsole(IntPtr hPC);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, IntPtr attrs, int size);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr prev, IntPtr retSize);
        [DllImport("kernel32.dll", SetLastError = true)] static extern void DeleteProcThreadAttributeList(IntPtr list);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CreateProcess(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string cwd, ref STARTUPINFOEX si, out PROCESS_INFORMATION pi);
        [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr h, out uint code);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    }
}
