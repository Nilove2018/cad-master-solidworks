using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;

namespace CADMaster
{
    /// <summary>The SOLIDWORKS add-in: ribbon tab, Tools menu, task pane and the CAD Master panel.</summary>
    [ComVisible(true)]
    [Guid("C4D7A1E2-5B39-4F6A-9E1D-2A8B7C3F5D10")]
    [ProgId("CADMaster.SwAddin")]
    public class SwAddin : ISwAddin, IPanelHost
    {
        const int MainGroupId = 7410;
        const string Title = "CAD Master";

        struct Cmd
        {
            public string Name, Hint, Id;
            public Cmd(string name, string hint, string id) { Name = name; Hint = hint; Id = id; }
        }

        /// <summary>Ribbon/menu commands. The order matches the icon strips made by tools\make_icons.ps1.</summary>
        static readonly Cmd[] Commands =
        {
            new Cmd("Open CAD Master", "Show the CAD Master panel", "open"),
            new Cmd("New Chat", "Start a new conversation with Claude", "new_chat"),
            new Cmd("New Design Brief", "Describe a new design; Claude plans it before building", "new_design"),
            new Cmd("Ask About Selection", "Attach the selected faces, edges, features or components", "ask_selection"),
            new Cmd("Send View", "Attach a screenshot of the current view", "send_view"),
            new Cmd("Attach File", "Attach an image or a file", "attach_file"),
            new Cmd("Review Model", "Claude reviews the active document", "review"),
            new Cmd("Make Drawing", "Claude makes a drawing of the active part", "drawing"),
            new Cmd("Run Checks", "Rebuild errors, What's Wrong, bodies, sketches, interference", "checks"),
            new Cmd("Export STEP / STL", "Export the active document", "export"),
            new Cmd("Mode", "Plan, ask, accept edits or auto", "mode"),
            new Cmd("Tasks", "Show the plan and task list", "tasks"),
            new Cmd("History", "Resume an earlier conversation", "history"),
            new Cmd("Terminal", "The full Claude Code command line", "terminal"),
            new Cmd("Account", "Sign in, switch account or sign out", "account"),
            new Cmd("Settings", "CAD Master settings", "settings"),
            new Cmd("Help", "Help and about", "help"),
        };

        static readonly int[][] TabBoxes = { new[] { 0, 1, 2 }, new[] { 3, 4, 5 }, new[] { 6, 7, 8, 9 }, new[] { 10, 11, 12, 13 }, new[] { 14, 15, 16 } };
        static readonly int[] IconSizes = { 20, 32, 40, 64, 96, 128 };

        ISldWorks app;
        int cookie;
        ICommandManager cmdMgr;
        TaskpaneView taskView;
        TaskPaneHost paneHost;
        PanelControl panel;
        Controller controller;
        PopoutForm popout;

        static SwAddin()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                var name = new AssemblyName(e.Name).Name;
                var path = Path.Combine(Path.GetDirectoryName(typeof(SwAddin).Assembly.Location), name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
        }

        // ------------------------------------------------------------------ IPanelHost
        public ISldWorks App => app;
        public int SwProcessId => Process.GetCurrentProcess().Id;

        public IWin32Window Owner
        {
            get
            {
                try { return new WindowHandle(new IntPtr(((Frame)app.Frame()).GetHWndx64())); } catch { return null; }
            }
        }

        public void ShowPanel()
        {
            if (popout != null) { popout.Activate(); return; }
            try { taskView?.ShowView(); } catch { }
        }

        public void TogglePopout()
        {
            if (popout == null)
            {
                paneHost.ShowPlaceholder(true);
                paneHost.Controls.Remove(panel);
                popout = new PopoutForm();
                popout.Controls.Add(panel);
                popout.FormClosing += (s, e) => DockBack();
                popout.Show(Owner);
            }
            else popout.Close();
        }

        void DockBack()
        {
            if (popout == null) return;
            popout.Controls.Remove(panel);
            paneHost.Controls.Add(panel);
            paneHost.ShowPlaceholder(false);
            popout = null;
            ShowPanel();
        }

        // ------------------------------------------------------------------ ISwAddin
        public bool ConnectToSW(object ThisSW, int Cookie)
        {
            try
            {
                Log.HookUnhandled();
                Log.Info("CAD Master " + Controller.Version + " loading into SOLIDWORKS PID " + System.Diagnostics.Process.GetCurrentProcess().Id);
                app = (ISldWorks)ThisSW;
                cookie = Cookie;
                app.SetAddinCallbackInfo2(0, this, cookie);
                Log.Info("connecting to SOLIDWORKS " + app.RevisionNumber());
                CreateTaskPane();
                Log.Info("task pane created");
                CreateCommands();
                Log.Info("commands created");
                ((SldWorks)app).ActiveModelDocChangeNotify += OnActiveDocChange;
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("ConnectToSW", ex);
                return false;
            }
        }

        public bool DisconnectFromSW()
        {
            Log.Info("unloading");
            try
            {
                ((SldWorks)app).ActiveModelDocChangeNotify -= OnActiveDocChange;
                showWait?.Unregister(null);
                showEvent?.Dispose();
                popout?.Close();
                controller?.Dispose();
                controller = null;
                Log.Info("unload: Claude, terminal and MCP server stopped");
                RemoveCommands();
                try { taskView?.DeleteView(); } catch { }
                Log.Info("unload: commands and task pane removed");
                paneHost?.Dispose();
                Log.Info("unload: panel disposed");
                if (taskView != null) Marshal.ReleaseComObject(taskView);
                if (cmdMgr != null) Marshal.ReleaseComObject(cmdMgr);
            }
            catch (Exception ex) { Log.Error("DisconnectFromSW", ex); }
            taskView = null; cmdMgr = null; app = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Log.Info("unloaded");
            return true;
        }

        int OnActiveDocChange()
        {
            controller?.OnActiveDocChanged();
            return 0;
        }

        void CreateTaskPane()
        {
            var icons = IconSizes.Select(s => Path.Combine(Paths.IconsDir, "taskpane_" + s + ".png")).ToArray();
            taskView = app.CreateTaskpaneView3(icons, "CAD Master: Claude Code for SOLIDWORKS");
            paneHost = new TaskPaneHost(this);
            taskView.DisplayWindowFromHandlex64(paneHost.Handle.ToInt64());
            panel = new PanelControl { Dock = DockStyle.Fill };
            paneHost.Controls.Add(panel);
            controller = new Controller(this, panel);
            StartWhenSolidWorksReady();
        }

        /// <summary>
        /// Starting the panel (browser view, tool server, model polling) while SOLIDWORKS was still starting
        /// up crashed SOLIDWORKS about 12 s after launch; loading the add-in into a running SOLIDWORKS was fine.
        /// So wait until SOLIDWORKS reports start-up complete, plus 3 s of quiet, then start.
        /// </summary>
        void StartWhenSolidWorksReady()
        {
            var timer = new Timer { Interval = 500 };
            int quietTicks = 0;
            timer.Tick += (s, e) =>
            {
                bool started;
                try { started = app == null || app.StartupProcessCompleted; } catch { started = true; }
                if (!started) { quietTicks = 0; return; }
                if (++quietTicks < 6) return;
                timer.Stop();
                timer.Dispose();
                if (controller == null) return; // unloaded meanwhile
                Log.Info("SOLIDWORKS start-up finished; starting the panel");
                try { controller.Start(); } catch (Exception ex) { Log.Error("controller start", ex); }
                if (System.Environment.GetEnvironmentVariable("CADMASTER_SHOW_PANEL") == "1") ShowPanelAfterStartup();
                ListenForShowRequests();
            };
            timer.Start();
        }

        /// <summary>
        /// Started from CAD Master.exe: show the panel. SOLIDWORKS opens its own Resources pane a few seconds
        /// after start-up and covers it, so show it again until that has happened.
        /// </summary>
        void ShowPanelAfterStartup()
        {
            Log.Info("show panel (started from CAD Master.exe)");
            ShowPanel();
            int ticks = 0;
            var again = new Timer { Interval = 2000 };
            again.Tick += (s, e) =>
            {
                if (++ticks >= 4 || controller == null) { again.Stop(); again.Dispose(); }
                if (controller != null) ShowPanel();
            };
            again.Start();
        }

        System.Threading.EventWaitHandle showEvent;
        System.Threading.RegisteredWaitHandle showWait;

        /// <summary>CAD Master.exe signals this event when SOLIDWORKS is already running: show the panel.</summary>
        void ListenForShowRequests()
        {
            try
            {
                showEvent = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, @"Local\CADMaster.ShowPanel");
                showWait = System.Threading.ThreadPool.RegisterWaitForSingleObject(showEvent, (s, timedOut) =>
                {
                    try { paneHost?.BeginInvoke((Action)(() => { Log.Info("show panel (CAD Master.exe)"); ShowPanel(); })); } catch { }
                }, null, -1, false);
            }
            catch (Exception ex) { Log.Error("show-panel event", ex); }
        }

        void CreateCommands()
        {
            cmdMgr = app.GetCommandManager(cookie);
            int[] known = Enumerable.Range(0, Commands.Length).ToArray();
            bool ignorePrevious = false;
            if (cmdMgr.GetGroupDataFromRegistry(MainGroupId, out object registryIds) && registryIds is int[] ids && !ids.SequenceEqual(known))
                ignorePrevious = true;

            int err = 0;
            var group = cmdMgr.CreateCommandGroup2(MainGroupId, Title, "CAD Master: Claude Code for SOLIDWORKS", "Claude Code inside SOLIDWORKS", -1, ignorePrevious, ref err);
            group.IconList = IconSizes.Select(s => Path.Combine(Paths.IconsDir, "toolbar_" + s + ".png")).ToArray();
            group.MainIconList = IconSizes.Select(s => Path.Combine(Paths.IconsDir, "main_" + s + ".png")).ToArray();
            int itemType = (int)(swCommandItemType_e.swMenuItem | swCommandItemType_e.swToolbarItem);
            for (int i = 0; i < Commands.Length; i++)
                group.AddCommandItem2(Commands[i].Name, -1, Commands[i].Hint, Commands[i].Name, i, "OnCmd" + i, "EnableAlways", i, itemType);
            group.HasToolbar = true;
            group.HasMenu = true;
            group.Activate();

            foreach (int docType in new[] { (int)swDocumentTypes_e.swDocPART, (int)swDocumentTypes_e.swDocASSEMBLY, (int)swDocumentTypes_e.swDocDRAWING })
            {
                var tab = cmdMgr.GetCommandTab(docType, Title);
                if (tab != null) cmdMgr.RemoveCommandTab(tab);
                tab = cmdMgr.AddCommandTab(docType, Title);
                foreach (var boxIds in TabBoxes)
                {
                    var box = tab.AddCommandTabBox();
                    var cmdIds = boxIds.Select(i => group.get_CommandID(i)).ToArray();
                    var textTypes = boxIds.Select(i => (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow).ToArray();
                    box.AddCommands(cmdIds, textTypes);
                }
            }
        }

        void RemoveCommands()
        {
            if (cmdMgr == null) return;
            foreach (int docType in new[] { (int)swDocumentTypes_e.swDocPART, (int)swDocumentTypes_e.swDocASSEMBLY, (int)swDocumentTypes_e.swDocDRAWING })
            {
                try { var tab = cmdMgr.GetCommandTab(docType, Title); if (tab != null) cmdMgr.RemoveCommandTab(tab); } catch { }
            }
            try { cmdMgr.RemoveCommandGroup2(MainGroupId, true); } catch { }
        }

        void Run(int index) { try { controller?.RunCommand(Commands[index].Id); } catch (Exception ex) { Log.Error("command " + index, ex); } }

        // Command callbacks are called by name through IDispatch, so they must be public.
        public void OnCmd0() => Run(0);
        public void OnCmd1() => Run(1);
        public void OnCmd2() => Run(2);
        public void OnCmd3() => Run(3);
        public void OnCmd4() => Run(4);
        public void OnCmd5() => Run(5);
        public void OnCmd6() => Run(6);
        public void OnCmd7() => Run(7);
        public void OnCmd8() => Run(8);
        public void OnCmd9() => Run(9);
        public void OnCmd10() => Run(10);
        public void OnCmd11() => Run(11);
        public void OnCmd12() => Run(12);
        public void OnCmd13() => Run(13);
        public void OnCmd14() => Run(14);
        public void OnCmd15() => Run(15);
        public void OnCmd16() => Run(16);
        public int EnableAlways() => 1;

        // ------------------------------------------------------------------ COM registration (RegAsm /codebase)
        [ComRegisterFunction]
        public static void Register(Type t)
        {
            var dir = Path.GetDirectoryName(t.Assembly.Location);
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\SolidWorks\AddIns\{" + t.GUID + "}"))
            {
                key.SetValue(null, 1);
                key.SetValue("Title", Title);
                key.SetValue("Description", "Claude Code inside SOLIDWORKS: plan, design and automate with Claude");
                key.SetValue("Icon Path", Path.Combine(dir, "icons", "addin_20.png"));
            }
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\SolidWorks\AddInsStartup\{" + t.GUID + "}"))
                key.SetValue(null, 1);
        }

        [ComUnregisterFunction]
        public static void Unregister(Type t)
        {
            try { Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\SolidWorks\AddIns\{" + t.GUID + "}", false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\SolidWorks\AddInsStartup\{" + t.GUID + "}", false); } catch { }
        }
    }

    sealed class WindowHandle : IWin32Window
    {
        public WindowHandle(IntPtr h) { Handle = h; }
        public IntPtr Handle { get; }
    }
}
