using System;
using System.Drawing;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

namespace CADMaster.TestHost
{
    /// <summary>
    /// Runs the CAD Master panel in its own window, attached to an already running SOLIDWORKS through the
    /// running object table. Used to develop and test without restarting SOLIDWORKS.
    /// Usage: CADMaster.TestHost.exe [solidworks-pid]
    /// </summary>
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            int pid = args.Length > 0 && int.TryParse(args[0], out int p) ? p : 0;
            Application.Run(new HostForm(pid));
        }
    }

    class HostForm : Form, IPanelHost
    {
        readonly ISldWorks app;
        readonly int pid;
        readonly Controller controller;

        public HostForm(int wantedPid)
        {
            Text = "CAD Master (test host)";
            Size = new Size(560, 900);
            BackColor = Color.FromArgb(14, 19, 34);
            try { app = SwRot.Connect(wantedPid, out pid); } catch (Exception ex) { Log.Error("ROT connect", ex); }
            Text += app == null ? " - SOLIDWORKS not found" : " - SOLIDWORKS PID " + pid;
            var panel = new PanelControl { Dock = DockStyle.Fill };
            Controls.Add(panel);
            controller = new Controller(this, panel);
            Load += (s, e) => controller.Start();
            FormClosed += (s, e) => controller.Dispose();
        }

        public ISldWorks App => app;
        public int SwProcessId => pid;
        public void ShowPanel() => Activate();
        public void TogglePopout() { }
        IWin32Window IPanelHost.Owner => this;
    }
}
