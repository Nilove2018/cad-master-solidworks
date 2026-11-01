using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CADMaster.Launcher
{
    /// <summary>
    /// "CAD Master.exe": the Start menu and desktop shortcut. CAD Master itself is an add-in DLL that only
    /// SOLIDWORKS can load, so this opens it the way a user would: it brings a running SOLIDWORKS to the
    /// front and asks the add-in to show its panel, or starts SOLIDWORKS with the panel open.
    /// </summary>
    static class Program
    {
        /// <summary>Signalled to make a running add-in show its panel (see SwAddin.ListenForShowRequests).</summary>
        const string ShowPanelEvent = @"Local\CADMaster.ShowPanel";

        [STAThread]
        static int Main()
        {
            var running = Process.GetProcessesByName("SLDWORKS").FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero)
                          ?? Process.GetProcessesByName("SLDWORKS").FirstOrDefault();
            if (running != null)
            {
                var hwnd = running.MainWindowHandle;
                if (hwnd != IntPtr.Zero)
                {
                    if (IsIconic(hwnd)) ShowWindow(hwnd, 9 /* SW_RESTORE */);
                    SetForegroundWindow(hwnd);
                }
                if (EventWaitHandle.TryOpenExisting(ShowPanelEvent, out var ev)) using (ev) ev.Set();
                else Message("SOLIDWORKS is running, but CAD Master isn't loaded in it.\n\nIn SOLIDWORKS, open Tools > Add-Ins and tick CAD Master.");
                return 0;
            }

            var exe = FindSolidWorks();
            if (exe == null)
            {
                Message("SOLIDWORKS isn't installed on this PC, or CAD Master can't find it.\n\nCAD Master is an add-in for SOLIDWORKS 2025.");
                return 1;
            }
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) };
            psi.EnvironmentVariables["CADMASTER_SHOW_PANEL"] = "1"; // the add-in opens its panel once SOLIDWORKS is ready
            Process.Start(psi);
            return 0;
        }

        /// <summary>The newest installed SOLIDWORKS, from HKLM\SOFTWARE\SolidWorks\SOLIDWORKS &lt;year&gt;\Setup.</summary>
        static string FindSolidWorks()
        {
            using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var sw = hklm.OpenSubKey(@"SOFTWARE\SolidWorks"))
            {
                if (sw == null) return null;
                foreach (var name in sw.GetSubKeyNames().Where(n => n.StartsWith("SOLIDWORKS 20")).OrderByDescending(n => n))
                    using (var setup = sw.OpenSubKey(name + @"\Setup"))
                    {
                        var dir = setup?.GetValue("SolidWorks Folder") as string;
                        var exe = dir == null ? null : Path.Combine(dir, "SLDWORKS.exe");
                        if (exe != null && File.Exists(exe)) return exe;
                    }
            }
            return null;
        }

        static void Message(string text) => MessageBox.Show(text, "CAD Master", MessageBoxButtons.OK, MessageBoxIcon.Information);

        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
    }
}
