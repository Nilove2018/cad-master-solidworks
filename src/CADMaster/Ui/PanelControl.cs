using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CADMaster
{
    /// <summary>The CAD Master panel: a WebView2 showing ui\index.html, with a JSON message bridge.</summary>
    public class PanelControl : UserControl
    {
        static bool loaderPathSet;
        readonly WebView2 web;
        readonly Queue<string> pending = new Queue<string>();
        bool ready;

        public event Action<string> MessageReceived;

        public PanelControl()
        {
            BackColor = Color.FromArgb(14, 19, 34);
            web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = BackColor };
            Controls.Add(web);
        }

        public async void Initialize()
        {
            try
            {
                if (!loaderPathSet)
                {
                    loaderPathSet = true;
                    try { CoreWebView2Environment.SetLoaderDllFolderPath(Paths.AddinDir); } catch (Exception ex) { Log.Error("SetLoaderDllFolderPath", ex); }
                }
                Directory.CreateDirectory(Paths.WebViewDir);
                // CADMASTER_DEBUG_PORT=9222 exposes the page over the DevTools protocol for automated UI tests.
                var debugPort = Environment.GetEnvironmentVariable("CADMASTER_DEBUG_PORT");
                var options = new CoreWebView2EnvironmentOptions(string.IsNullOrEmpty(debugPort) ? null : "--remote-debugging-port=" + debugPort);
                var env = await CoreWebView2Environment.CreateAsync(null, Paths.WebViewDir, options);
                await web.EnsureCoreWebView2Async(env);
                var core = web.CoreWebView2;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreDevToolsEnabled = true;
                core.Settings.IsZoomControlEnabled = true;
                core.SetVirtualHostNameToFolderMapping("cadmaster.local", Paths.UiDir, CoreWebView2HostResourceAccessKind.Allow);
                core.WebMessageReceived += (s, e) =>
                {
                    try { MessageReceived?.Invoke(WithFilePaths(e)); } catch (Exception ex) { Log.Error("ui message", ex); }
                };
                core.NewWindowRequested += (s, e) => { e.Handled = true; OpenExternal(e.Uri); };
                core.NavigationStarting += (s, e) =>
                {
                    if (!e.Uri.StartsWith("https://cadmaster.local/", StringComparison.OrdinalIgnoreCase)) { e.Cancel = true; OpenExternal(e.Uri); }
                };
                core.NavigationCompleted += (s, e) =>
                {
                    Log.Info("panel page loaded (" + (e.IsSuccess ? "ok" : "error " + e.WebErrorStatus) + ")");
                    ready = true;
                    try { while (pending.Count > 0) core.PostWebMessageAsJson(pending.Dequeue()); }
                    catch (Exception ex) { Log.Error("post pending", ex); }
                };
                core.ProcessFailed += (s, e) => Log.Error("WebView2 process failed: " + e.ProcessFailedKind + " " + e.Reason);
                core.Navigate("https://cadmaster.local/index.html");
            }
            catch (Exception ex)
            {
                Log.Error("WebView2 init", ex);
                Controls.Clear();
                Controls.Add(new Label
                {
                    Dock = DockStyle.Fill, ForeColor = Color.White, Padding = new Padding(12),
                    Text = "CAD Master couldn't start its browser view (WebView2).\r\n\r\n" + ex.Message + "\r\n\r\nLog: " + Log.FilePath,
                });
            }
        }

        /// <summary>
        /// Files dropped on the page arrive as postMessageWithAdditionalObjects: the page can't see their
        /// paths, the host can. Adds them to the message as "paths".
        /// </summary>
        static string WithFilePaths(CoreWebView2WebMessageReceivedEventArgs e)
        {
            var json = e.WebMessageAsJson;
            var extra = e.AdditionalObjects;
            if (extra == null || extra.Count == 0) return json;
            var paths = new List<object>();
            foreach (var o in extra)
                if (o is CoreWebView2File f && !string.IsNullOrEmpty(f.Path)) paths.Add(f.Path);
            var m = Json.ParseObject(json);
            if (m == null) return json;
            m["paths"] = paths;
            return Json.Serialize(m);
        }

        /// <summary>Starts a Windows drag of real files, so they can be dropped on Explorer, the desktop or an email.</summary>
        public void DragFiles(string[] paths)
        {
            try { DoDragDrop(new DataObject(DataFormats.FileDrop, paths), DragDropEffects.Copy); }
            catch (Exception ex) { Log.Error("drag out", ex); }
        }

        static void OpenExternal(string uri)
        {
            if (uri != null && uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                try { System.Diagnostics.Process.Start(uri); } catch { }
        }

        /// <summary>Sends a JSON message to the page (any thread).</summary>
        public void Post(string json)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke((Action)(() => Post(json))); } catch { } return; }
            if (!ready || web.CoreWebView2 == null) { pending.Enqueue(json); return; }
            try { web.CoreWebView2.PostWebMessageAsJson(json); } catch (Exception ex) { Log.Error("post", ex); }
        }

        public void FocusWeb() { try { web.Focus(); } catch { } }
    }
}
