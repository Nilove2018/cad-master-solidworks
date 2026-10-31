using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CADMaster
{
    /// <summary>The window SOLIDWORKS shows in its task pane. Holds the panel, or a placeholder while it is popped out.</summary>
    public class TaskPaneHost : UserControl
    {
        readonly Label placeholder;

        public TaskPaneHost(IPanelHost host)
        {
            BackColor = Color.FromArgb(14, 19, 34);
            placeholder = new Label
            {
                Dock = DockStyle.Fill, Visible = false, Cursor = Cursors.Hand,
                ForeColor = Color.FromArgb(243, 231, 207), TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 10f), Text = "CAD Master is open in its own window.\r\n\r\nClick here to dock it back.",
            };
            placeholder.Click += (s, e) => host.TogglePopout();
            Controls.Add(placeholder);
            CreateControl();
        }

        public void ShowPlaceholder(bool show)
        {
            placeholder.Visible = show;
            if (show) placeholder.BringToFront();
        }
    }

    /// <summary>A free-floating CAD Master window (bigger than the task pane).</summary>
    public class PopoutForm : Form
    {
        public PopoutForm()
        {
            Text = "CAD Master";
            BackColor = Color.FromArgb(14, 19, 34);
            StartPosition = FormStartPosition.Manual;
            var area = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(560, Math.Min(900, area.Height - 80));
            Location = new Point(area.Right - Width - 40, area.Top + 40);
            ShowInTaskbar = false;
            try
            {
                var png = Path.Combine(Paths.IconsDir, "taskpane_32.png");
                if (File.Exists(png)) using (var bmp = new Bitmap(png)) Icon = Icon.FromHandle(bmp.GetHicon());
            }
            catch { }
        }
    }
}
