using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

namespace CADMaster
{
    /// <summary>What the controller needs from its host: the add-in inside SOLIDWORKS, or the stand-alone test host.</summary>
    public interface IPanelHost
    {
        ISldWorks App { get; }
        int SwProcessId { get; }
        void ShowPanel();
        void TogglePopout();
        IWin32Window Owner { get; }
    }

    /// <summary>Connects the panel UI, Claude Code (chat and terminal), the MCP server and the SOLIDWORKS tools.</summary>
    public sealed class Controller : IDisposable
    {
        readonly IPanelHost host;
        readonly PanelControl panel;
        readonly SwTools tools;
        readonly Settings settings = Settings.Load();
        readonly System.Windows.Forms.Timer contextTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        McpServer mcp;
        string mcpConfigPath;
        ClaudeSession session;
        TerminalSession terminal;
        string workspace, resumeId, lastSessionId, lastContext;
        string mode, model, effort;
        int termCols = 100, termRows = 30;
        bool killRequested, contextStale;
        PythonInfo python;                       // from the last setup check
        Dictionary<string, object> swTest;       // result of the setup's SOLIDWORKS test, this session
        volatile string fixRunning;              // id of the setup fix that is running

        /// <summary>The product version (VERSION file), not the fixed COM AssemblyVersion.</summary>
        public static string Version =>
            typeof(Controller).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(Controller).Assembly.GetName().Version.ToString(3);

        public Controller(IPanelHost host, PanelControl panel)
        {
            this.host = host;
            this.panel = panel;
            tools = new SwTools(() => host.App ?? throw new InvalidOperationException("SOLIDWORKS is not connected."));
            mode = settings.PlanFirst ? "plan" : settings.DefaultMode;
            model = settings.DefaultModel;
            effort = settings.DefaultEffort;
        }

        public void Start()
        {
            try
            {
                mcp = new McpServer(SwTools.Definitions, (name, args) => OnSw(() => tools.Call(name, args), 120000));
                mcp.Start();
                mcpConfigPath = mcp.WriteConfig(Path.Combine(Paths.RuntimeDir, "mcp-" + System.Diagnostics.Process.GetCurrentProcess().Id + ".json"));
            }
            catch (Exception ex) { Log.Error("MCP server", ex); }
            panel.MessageReceived += OnUiMessage;
            panel.Initialize();
            contextTimer.Tick += (s, e) => OnContextTimer();
            contextTimer.Start();
        }

        // ------------------------------------------------------------------ helpers
        void Post(string type, params object[] kv)
        {
            var d = Json.O(kv);
            d["type"] = type;
            panel.Post(Json.Serialize(d));
        }

        void Toast(string level, string text) => Post("toast", "level", level, "text", text);

        /// <summary>Runs SOLIDWORKS API code on the SOLIDWORKS main thread, waiting at most timeoutMs.</summary>
        T OnSw<T>(Func<T> func, int timeoutMs = 30000)
        {
            if (!panel.InvokeRequired) return func();
            T result = default(T);
            Exception error = null;
            var done = new ManualResetEventSlim();
            panel.BeginInvoke((Action)(() =>
            {
                try { result = func(); } catch (Exception ex) { error = ex; } finally { done.Set(); }
            }));
            if (!done.Wait(timeoutMs)) throw new TimeoutException("SOLIDWORKS is busy (a dialog may be open, or a command is running).");
            if (error != null) throw error;
            return result;
        }

        Dictionary<string, object> SafeStatus()
        {
            try { return host.App == null ? null : tools.Status(); } catch (Exception ex) { Log.Error("status", ex); return null; }
        }

        /// <summary>Sends the active document and selection count to the header when they change.</summary>
        public void PushContext(bool force)
        {
            if (host.App == null) return;
            try
            {
                string title = "", path = "";
                int selected = 0;
                if (host.App.ActiveDoc is ModelDoc2 doc)
                {
                    title = doc.GetTitle();
                    path = doc.GetPathName();
                    selected = ((SelectionMgr)doc.SelectionManager)?.GetSelectedObjectCount2(-1) ?? 0;
                }
                var key = title + "|" + path + "|" + selected;
                if (!force && key == lastContext) return;
                lastContext = key;
                Post("context", "title", title, "path", path, "selected", selected);
            }
            catch { }
        }

        /// <summary>
        /// Called from SOLIDWORKS' ActiveModelDocChangeNotify. That fires in the middle of opening and
        /// closing documents, and calling back into SOLIDWORKS from inside it crashed SOLIDWORKS
        /// (mfc140u.dll access violation during a scripted CloseDoc). Only mark the context stale here;
        /// the timer reads it later, once SOLIDWORKS is idle.
        /// </summary>
        public void OnActiveDocChanged() => contextStale = true;

        void OnContextTimer()
        {
            try
            {
                // While Claude is working its scripts drive SOLIDWORKS; don't read the model in between.
                if (session != null && session.Busy) return;
                var force = contextStale;
                contextStale = false;
                PushContext(force);
            }
            catch (Exception ex) { Log.Error("context timer", ex); }
        }

        /// <summary>A ribbon or menu command: show the panel and let the page act on it.</summary>
        public void RunCommand(string id)
        {
            host.ShowPanel();
            panel.FocusWeb();
            Post("command", "id", id);
        }

        string ClaudeExe() => ClaudeLocator.Find(settings);

        string ComputeWorkspace()
        {
            string folder = null;
            if (settings.WorkspaceMode == "activeDoc" && host.App?.ActiveDoc is ModelDoc2 doc)
            {
                var p = doc.GetPathName();
                if (!string.IsNullOrEmpty(p)) folder = Path.GetDirectoryName(p);
            }
            if (string.IsNullOrEmpty(folder)) folder = string.IsNullOrWhiteSpace(settings.WorkspaceFolder) ? Paths.DefaultWorkspace : settings.WorkspaceFolder;
            Directory.CreateDirectory(folder);
            return folder;
        }

        string Workspace() => workspace ?? (workspace = ComputeWorkspace());

        Dictionary<string, string> ChildEnv()
        {
            var env = new Dictionary<string, string>
            {
                { "CADMASTER", "1" },
                { "CADMASTER_VERSION", Version },
                { "CADMASTER_SW_PID", host.SwProcessId.ToString() },
                { "PYTHONIOENCODING", "utf-8" },
            };
            if (mcp != null) env["CADMASTER_MCP_URL"] = mcp.Url;
            if (python != null) env["CADMASTER_PYTHON"] = python.Exe;
            var py = System.Environment.GetEnvironmentVariable("PYTHONPATH");
            env["PYTHONPATH"] = string.IsNullOrEmpty(py) ? Paths.PythonDir : py + ";" + Paths.PythonDir;
            return env;
        }

        string SystemPrompt()
        {
            var template = Path.Combine(Paths.PluginDir, "system-prompt.md");
            var text = File.Exists(template) ? File.ReadAllText(template) : "You are running inside SOLIDWORKS through the CAD Master add-in.";
            string swVersion = "";
            try { swVersion = host.App?.RevisionNumber() ?? ""; } catch { }
            var py = python ?? (python = PythonLocator.Find(settings));
            return text.Replace("{{SW_VERSION}}", swVersion)
                       .Replace("{{SW_PID}}", host.SwProcessId.ToString())
                       .Replace("{{WORKSPACE}}", Workspace())
                       .Replace("{{PYTHON_DIR}}", Paths.PythonDir)
                       .Replace("{{PYTHON}}", py != null ? "`" + py.Command + "` (Python " + py.Version + (py.Pywin32 != null ? ", pywin32 " + py.Pywin32 : ", pywin32 NOT installed: tell the user to open Setup") + ")"
                                                        : "not found on this PC: tell the user to open Setup (menu > Setup check) and install Python")
                       .Replace("{{SWLIB}}", SkillLocator.FindSwlib(settings) is string swlib ? "`" + swlib + "`" : "the solidworks-api skill's `examples\\swlib.py` (not found on this PC: tell the user to install the design rules in Setup)")
                       .Replace("{{EXTRA}}", string.IsNullOrWhiteSpace(settings.ExtraInstructions) ? "(none)" : settings.ExtraInstructions);
        }

        string WriteSystemPrompt()
        {
            var path = Path.Combine(Paths.RuntimeDir, "system-prompt-" + System.Diagnostics.Process.GetCurrentProcess().Id + ".md");
            Directory.CreateDirectory(Paths.RuntimeDir);
            File.WriteAllText(path, SystemPrompt());
            return path;
        }

        // ------------------------------------------------------------------ chat session
        void EnsureSession()
        {
            if (session != null && session.Running) return;
            var exe = ClaudeExe() ?? throw new InvalidOperationException("Claude Code isn't installed. Open Setup (menu > Setup check) and choose Install Claude Code.");
            var ws = Workspace();
            var resume = resumeId ?? lastSessionId;
            var o = new ClaudeSessionOptions
            {
                Exe = exe, WorkDir = ws, Model = model, Effort = effort, Mode = mode,
                ResumeId = resume, NewSessionId = resume == null ? Guid.NewGuid().ToString() : null,
                McpConfigPath = mcp == null ? null : mcpConfigPath,
                SystemPromptPath = WriteSystemPrompt(),
                PluginDir = Paths.PluginDir,
                AllowBypass = true, // only makes Bypass selectable; the panel asks the user before turning it on
                AllowedTools = SwTools.ReadOnlyTools.Select(t => "mcp__solidworks__" + t).ToArray(),
                Env = ChildEnv(),
            };
            var s = new ClaudeSession();
            s.Message += (raw, m) =>
            {
                panel.Post("{\"type\":\"claude\",\"msg\":" + raw + "}");
                if (m.Str("type") == "result") Post("session", "state", "idle", "sessionId", s.SessionId);
            };
            s.PermissionRequest += (id, req) => Post("permission", "requestId", id, "request", req);
            s.Exited += (code, tail) =>
            {
                lastSessionId = s.SessionId ?? lastSessionId;
                bool expected = killRequested;
                killRequested = false;
                if (session == s) session = null;
                Post("session", "state", "stopped", "sessionId", lastSessionId, "code", code,
                    "error", expected || code == 0 ? null : Tail(tail));
                s.Dispose();
            };
            s.Start(o);
            resumeId = null;
            session = s;
            Post("session", "state", "starting", "sessionId", s.SessionId, "workspace", ws, "mode", mode, "model", model);
        }

        static string Tail(string s) => string.IsNullOrEmpty(s) ? null : s.Length > 1500 ? s.Substring(s.Length - 1500) : s;

        void StopSession()
        {
            if (session == null) return;
            lastSessionId = session.SessionId ?? lastSessionId;
            killRequested = true;
            var s = session;
            session = null;
            s.Dispose();
        }

        // ------------------------------------------------------------------ terminal
        void StartTerminal(List<string> args, string title)
        {
            var exe = ClaudeExe();
            if (exe == null) { Toast("error", "Claude Code isn't installed."); return; }
            terminal?.Dispose();
            var t = new TerminalSession();
            t.Output += data => Post("term_data", "data", data);
            t.Exited += code => { Post("term_exit", "code", code, "title", title); };
            t.Start(ProcessUtil.Quote(exe) + " " + ProcessUtil.Join(args), Workspace(), termCols, termRows, ChildEnv());
            terminal = t;
            Post("term_started", "title", title);
        }

        List<string> InteractiveArgs(string resume)
        {
            var args = new List<string>();
            if (mcp != null) { args.Add("--mcp-config"); args.Add(mcpConfigPath); }
            args.Add("--append-system-prompt"); args.Add(SystemPrompt());
            if (Directory.Exists(Paths.PluginDir)) { args.Add("--plugin-dir"); args.Add(Paths.PluginDir); }
            if (!string.IsNullOrEmpty(model) && model != "default") { args.Add("--model"); args.Add(model); }
            if (!string.IsNullOrEmpty(resume)) { args.Add("--resume"); args.Add(resume); }
            return args;
        }

        // ------------------------------------------------------------------ account and setup
        int setupRun;

        /// <summary>Runs the setup checks in the background; posts "auth" (account) and "setup" (the checklist).</summary>
        void RefreshAuth()
        {
            var run = Interlocked.Increment(ref setupRun);
            Task.Run(() =>
            {
                try
                {
                    var rep = SetupChecks.Run(settings, host.App != null, swTest);
                    if (run != setupRun) return; // a newer check is on its way
                    python = rep.Python;
                    panel.Post(Json.Serialize(Json.O("type", "auth").Concat(rep.Auth).ToDictionary(kv => kv.Key, kv => kv.Value)));
                    Post("setup", "items", rep.Items.Select(i => i.ToJson()).ToArray(), "ready", rep.Ready, "done", settings.SetupDone, "running", fixRunning);
                }
                catch (Exception ex)
                {
                    Log.Error("setup check", ex);
                    Post("auth", "installed", ClaudeExe() != null, "loggedIn", false, "error", ex.Message);
                }
            });
        }

        /// <summary>Runs one setup fix in the background, then checks everything again.</summary>
        void RunFix(string id)
        {
            if (fixRunning != null) { Toast("warn", "Wait until the current setup step has finished."); return; }
            if (id == "run_sw_test")
            {
                if (host.App == null) { Toast("warn", "The SOLIDWORKS test runs inside SOLIDWORKS."); return; }
                if (session != null && session.Busy) { Toast("warn", "Wait until Claude has finished, then run the test."); return; }
            }
            fixRunning = id;
            Post("setup_fix", "id", id, "state", "running");
            Log.Info("setup fix " + id);
            Task.Run(() =>
            {
                FixResult r;
                try
                {
                    switch (id)
                    {
                        case "install_claude": r = SetupFixes.InstallClaude(); break;
                        case "install_python": r = SetupFixes.InstallPython(); break;
                        case "install_pywin32": r = SetupFixes.InstallPywin32(python ?? PythonLocator.Find(settings)); break;
                        case "install_skill": r = SetupFixes.InstallSkill(ClaudeExe()); break;
                        case "install_git": r = SetupFixes.InstallGit(); break;
                        case "run_sw_test":
                            var py = python ?? PythonLocator.Find(settings);
                            swTest = SetupFixes.SolidWorksTest(py, SkillLocator.FindSwlib(settings), ChildEnv());
                            r = new FixResult { Ok = swTest.Bool("ok"), Message = swTest.Bool("ok") ? "SOLIDWORKS test passed" : "SOLIDWORKS test failed", Log = swTest.Str("error") };
                            break;
                        default: r = new FixResult { Message = "Unknown setup step " + id }; break;
                    }
                }
                catch (Exception ex) { Log.Error("setup fix " + id, ex); r = new FixResult { Message = ex.Message }; }
                Log.Info("setup fix " + id + (r.Ok ? " ok" : " failed: " + r.Message + "\n" + r.Log));
                fixRunning = null;
                Post("setup_fix", "id", id, "state", r.Ok ? "ok" : "fail", "message", r.Message, "log", r.Log);
                if (!r.Ok && r.Url != null) OpenUrl(r.Url);
                RefreshAuth();
            });
        }

        // ------------------------------------------------------------------ UI messages
        void OnUiMessage(string json)
        {
            var m = Json.ParseObject(json);
            if (m == null) return;
            var type = m.Str("type");
            try { Handle(type, m); }
            catch (Exception ex)
            {
                Log.Error("ui " + type, ex);
                Toast("error", ex.Message);
            }
        }

        void Handle(string type, Dictionary<string, object> m)
        {
            switch (type)
            {
                case "ready":
                    Post("hello", "version", Version, "settings", settings, "workspace", Workspace(), "sw", SafeStatus(),
                        "mode", mode, "model", model, "effort", effort, "mcp", mcp != null, "swConnected", host.App != null);
                    PushContext(true);
                    RefreshAuth();
                    break;

                case "send":
                    killRequested = false;
                    EnsureSession();
                    session.SendUser(m.Arr("content") ?? new object[0]);
                    Post("session", "state", "busy", "sessionId", session.SessionId);
                    break;

                case "interrupt":
                    if (session == null) break;
                    session.Interrupt();
                    var s = session;
                    Task.Delay(8000).ContinueWith(_ => { if (session == s && s.Busy) { StopSession(); Post("session", "state", "stopped", "sessionId", lastSessionId); } });
                    break;

                case "permission_response":
                    session?.RespondPermission(m.Str("requestId"), m.Obj("response"));
                    break;

                case "set_mode":
                    mode = m.Str("mode") ?? mode;
                    if (mode == "bypassPermissions" && !settings.AllowBypass) { mode = "default"; Toast("warn", "Turn on Bypass mode in Settings first."); }
                    if (session != null && session.Running) session.SetMode(mode);
                    Post("mode", "mode", mode);
                    break;

                case "set_model":
                    model = m.Str("model") ?? "default";
                    if (session != null && session.Running) session.SetModel(model);
                    break;

                case "set_effort":
                    effort = m.Str("effort") ?? "";
                    if (session != null && !session.Busy) StopSession(); // effort is a start-up flag: resume with it on the next message
                    break;

                case "new_chat":
                    StopSession();
                    lastSessionId = null;
                    resumeId = null;
                    workspace = null;
                    mode = settings.PlanFirst && m.Bool("design") ? "plan" : m.Str("mode") ?? mode;
                    Post("session", "state", "none", "workspace", Workspace(), "mode", mode);
                    break;

                case "list_sessions":
                    Post("sessions", "workspace", Workspace(), "list", SessionStore.List(Workspace()));
                    break;

                case "resume":
                    StopSession();
                    resumeId = m.Str("sessionId");
                    lastSessionId = resumeId;
                    Post("transcript", "sessionId", resumeId, "lines", SessionStore.Load(Workspace(), resumeId));
                    break;

                case "attach_view":
                    var view = m.Str("view", "current");
                    var png = OnSw(() => tools.Screenshot(view, view != "current", 1400), 60000);
                    Post("attachment", "kind", "image", "mediaType", "image/png", "name", "SOLIDWORKS " + view + " view", "data", Convert.ToBase64String(png));
                    break;

                case "attach_selection":
                    var text = OnSw(() => tools.SelectionSummary());
                    Post("attachment", "kind", "text", "name", "Selection", "text", text);
                    break;

                case "attach_file":
                    using (var dlg = new OpenFileDialog { Title = "Attach files", Multiselect = true,
                        Filter = "Pictures and CAD files|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp;*.step;*.stp;*.igs;*.iges;*.stl;*.3mf;*.x_t;*.x_b;*.sldprt;*.sldasm;*.slddrw;*.pdf;*.dxf;*.dwg|All files|*.*" })
                    {
                        if (dlg.ShowDialog(host.Owner) != DialogResult.OK) break;
                        foreach (var f in dlg.FileNames) Attach(f);
                    }
                    break;

                case "drop_files":
                case "attach_paths":
                    foreach (var p in (m.Arr("paths") ?? new object[0]).OfType<string>()) Attach(p);
                    break;

                case "drag_out":
                    var dragPath = m.Str("path") ?? SaveDraggedImage(m.Str("data"), m.Str("name"), m.Str("mediaType"));
                    if (dragPath != null && (File.Exists(dragPath) || Directory.Exists(dragPath)))
                        panel.BeginInvoke((Action)(() => panel.DragFiles(new[] { dragPath })));
                    break;

                case "open_file":
                    OpenFile(m.Str("path"));
                    break;

                case "sw_status":
                    Post("sw_status", "sw", SafeStatus());
                    break;

                case "auth_status": RefreshAuth(); break;

                case "auth_login":
                    var how = m.Str("method", "claudeai");
                    StartTerminal(new List<string> { "auth", "login", "--" + (how == "console" ? "console" : how == "sso" ? "sso" : "claudeai") }, "Sign in");
                    break;

                case "auth_logout":
                    StopSession();
                    var exe = ClaudeExe();
                    if (exe != null) ProcessUtil.Run(exe, new[] { "auth", "logout" });
                    RefreshAuth();
                    break;

                case "install_claude": RunFix("install_claude"); break;

                case "setup_check": RefreshAuth(); break;

                case "setup_fix": RunFix(m.Str("id")); break;

                case "setup_done":
                    settings.SetupDone = true;
                    settings.Save();
                    Post("settings", "settings", settings);
                    break;

                case "term_start":
                    var resumeTerm = m.Str("resume");
                    if (resumeTerm != null) StopSession(); // a session can only be open in one place
                    StartTerminal(InteractiveArgs(resumeTerm), "Claude Code");
                    break;

                case "term_input": terminal?.Write(m.Str("data") ?? ""); break;

                case "term_resize":
                    termCols = Math.Max(20, m.Int("cols", termCols));
                    termRows = Math.Max(5, m.Int("rows", termRows));
                    terminal?.Resize(termCols, termRows);
                    break;

                case "term_stop": terminal?.Dispose(); terminal = null; Post("term_exit", "code", 0); break;

                case "save_settings":
                    var incoming = Json.Deserialize<Settings>(Json.Serialize(m.Obj("settings")));
                    foreach (var p in typeof(Settings).GetProperties()) p.SetValue(settings, p.GetValue(incoming));
                    settings.Save();
                    Post("settings", "settings", settings);
                    Toast("ok", "Settings saved");
                    break;

                case "pick_workspace":
                    using (var fb = new FolderBrowserDialog { Description = "Folder Claude works in (scripts, exports, notes)", SelectedPath = Workspace() })
                    {
                        if (fb.ShowDialog(host.Owner) != DialogResult.OK) break;
                        settings.WorkspaceFolder = fb.SelectedPath;
                        settings.WorkspaceMode = "fixed";
                        settings.Save();
                        StopSession();
                        lastSessionId = null;
                        workspace = null;
                        Post("settings", "settings", settings);
                        Post("session", "state", "none", "workspace", Workspace());
                    }
                    break;

                case "open_path":
                    var path = m.Str("path");
                    if (File.Exists(path)) System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\"");
                    else if (Directory.Exists(path)) System.Diagnostics.Process.Start("explorer.exe", "\"" + path + "\"");
                    break;

                case "open_logs": System.Diagnostics.Process.Start("explorer.exe", "\"" + Paths.LogDir + "\""); break;

                case "open_url": OpenUrl(m.Str("url")); break;

                case "export_chat":
                    using (var save = new SaveFileDialog { Title = "Export chat", Filter = "Markdown (*.md)|*.md", FileName = m.Str("name", "CAD Master chat") + ".md", InitialDirectory = Workspace() })
                    {
                        if (save.ShowDialog(host.Owner) == DialogResult.OK) { File.WriteAllText(save.FileName, m.Str("markdown") ?? ""); Toast("ok", "Chat exported"); }
                    }
                    break;

                case "popout": host.TogglePopout(); break;
            }
        }

        // ------------------------------------------------------------------ files in and out
        static readonly HashSet<string> ImageExts = new HashSet<string> { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".tif", ".tiff" };
        static readonly Dictionary<string, string> FileKinds = new Dictionary<string, string>
        {
            { ".step", "STEP 3D model" }, { ".stp", "STEP 3D model" }, { ".igs", "IGES 3D model" }, { ".iges", "IGES 3D model" },
            { ".x_t", "Parasolid model" }, { ".x_b", "Parasolid model" }, { ".stl", "STL mesh" }, { ".3mf", "3MF mesh" }, { ".obj", "OBJ mesh" },
            { ".sldprt", "SOLIDWORKS part" }, { ".sldasm", "SOLIDWORKS assembly" }, { ".slddrw", "SOLIDWORKS drawing" },
            { ".pdf", "PDF document" }, { ".dxf", "DXF 2D drawing" }, { ".dwg", "DWG 2D drawing" },
            { ".csv", "table" }, { ".xlsx", "Excel workbook" }, { ".txt", "text file" }, { ".md", "notes" },
        };

        /// <summary>Attaches a file the user dropped or picked: pictures are sent so Claude sees them, other files by path.</summary>
        void Attach(string path)
        {
            if (Directory.Exists(path)) { Post("attachment", "kind", "text", "name", Path.GetFileName(path), "path", path, "text", "Folder: " + path); return; }
            if (!File.Exists(path)) return;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ImageExts.Contains(ext))
            {
                try
                {
                    var data = LoadImageForClaude(path, out var mediaType);
                    Post("attachment", "kind", "image", "mediaType", mediaType, "name", Path.GetFileName(path), "path", path, "data", data);
                    return;
                }
                catch (Exception ex) { Log.Error("image " + path, ex); }
            }
            FileKinds.TryGetValue(ext, out var kind);
            Post("attachment", "kind", "text", "name", Path.GetFileName(path), "path", path, "text", "Attached " + (kind ?? "file") + ": " + path);
        }

        /// <summary>Pictures larger than Claude needs are scaled to 1600 px on the long side.</summary>
        static string LoadImageForClaude(string path, out string mediaType)
        {
            const int max = 1600;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var bytes = File.ReadAllBytes(path);
            if (ext == ".webp" && bytes.Length < 3500000) { mediaType = "image/webp"; return Convert.ToBase64String(bytes); }
            using (var ms = new MemoryStream(bytes))
            using (var src = System.Drawing.Image.FromStream(ms))
            {
                bool web = ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif";
                if (web && Math.Max(src.Width, src.Height) <= max && bytes.Length < 3500000)
                {
                    mediaType = ext == ".jpg" || ext == ".jpeg" ? "image/jpeg" : "image/" + ext.Substring(1);
                    return Convert.ToBase64String(bytes);
                }
                double k = Math.Min(1.0, (double)max / Math.Max(src.Width, src.Height));
                using (var bmp = new System.Drawing.Bitmap(src, Math.Max(1, (int)(src.Width * k)), Math.Max(1, (int)(src.Height * k))))
                using (var outMs = new MemoryStream())
                {
                    bool photo = ext == ".jpg" || ext == ".jpeg";
                    if (!photo)
                    {
                        bmp.Save(outMs, System.Drawing.Imaging.ImageFormat.Png);
                        if (outMs.Length > 1500000) { photo = true; outMs.SetLength(0); } // photo-like picture: JPEG is far smaller
                    }
                    if (photo)
                    {
                        var jpeg = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");
                        var q = new System.Drawing.Imaging.EncoderParameters(1);
                        q.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 88L);
                        using (var flat = new System.Drawing.Bitmap(bmp.Width, bmp.Height)) // JPEG has no transparency: flatten on white
                        {
                            using (var g = System.Drawing.Graphics.FromImage(flat)) { g.Clear(System.Drawing.Color.White); g.DrawImage(bmp, 0, 0, bmp.Width, bmp.Height); }
                            flat.Save(outMs, jpeg, q);
                        }
                    }
                    mediaType = photo ? "image/jpeg" : "image/png";
                    return Convert.ToBase64String(outMs.ToArray());
                }
            }
        }

        /// <summary>A model view dragged out of the panel becomes a real PNG in Outputs\Views of the workspace.</summary>
        string SaveDraggedImage(string base64, string name, string mediaType)
        {
            if (string.IsNullOrEmpty(base64)) return null;
            var dir = Path.Combine(Workspace(), "Outputs", "Views");
            Directory.CreateDirectory(dir);
            var stem = new string((name ?? "model view").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
            if (stem.Length == 0 || stem.Length > 60) stem = "model view";
            stem = Path.GetFileNameWithoutExtension(stem);
            var path = Path.Combine(dir, stem + " " + DateTime.Now.ToString("yyyy-MM-dd HHmmss") + (mediaType == "image/jpeg" ? ".jpg" : ".png"));
            File.WriteAllBytes(path, Convert.FromBase64String(base64));
            return path;
        }

        static readonly HashSet<string> ShellOpenExts = new HashSet<string> { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".pdf", ".stl", ".3mf", ".obj", ".dxf", ".dwg", ".csv", ".xlsx", ".txt", ".md", ".html", ".htm" };

        /// <summary>Opens a file chip: SOLIDWORKS documents and neutral CAD files in this SOLIDWORKS, other files in their own app.</summary>
        void OpenFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) { Toast("warn", "File not found: " + path); return; }
            var ext = Path.GetExtension(path).ToLowerInvariant();
            int docType = ext == ".sldprt" ? 1 : ext == ".sldasm" ? 2 : ext == ".slddrw" ? 3 : 0;
            if (docType != 0 || FileKinds.ContainsKey(ext) && FileKinds[ext].EndsWith("model"))
            {
                if (host.App == null) { Toast("error", "SOLIDWORKS is not connected."); return; }
                var app = host.App;
                ModelDoc2 doc;
                int errors = 0, warnings = 0;
                if (docType != 0) doc = app.OpenDoc6(path, docType, 1 /* swOpenDocOptions_Silent */, "", ref errors, ref warnings);
                else doc = app.LoadFile4(path, "r", null, ref errors) as ModelDoc2;
                if (doc == null) { Toast("error", "SOLIDWORKS couldn't open " + Path.GetFileName(path) + " (error " + errors + ")"); return; }
                int activateErr = 0;
                app.ActivateDoc3(doc.GetTitle(), false, 0, ref activateErr);
                return;
            }
            if (ShellOpenExts.Contains(ext)) System.Diagnostics.Process.Start(path);
            else System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\"");
        }

        static void OpenUrl(string url)
        {
            if (url != null && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) System.Diagnostics.Process.Start(url);
        }

        public void Dispose()
        {
            contextTimer.Stop();
            contextTimer.Dispose();
            StopSession();
            terminal?.Dispose();
            mcp?.Dispose();
            try { if (mcpConfigPath != null) File.Delete(mcpConfigPath); } catch { }
        }
    }
}
