using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace CADMaster
{
    public sealed class ClaudeSessionOptions
    {
        public string Exe;
        public string WorkDir;
        public string Model;
        public string Effort;
        public string Mode = "default";
        public string ResumeId;
        public string NewSessionId;
        public string McpConfigPath;
        public string SystemPromptPath;
        public string PluginDir;
        public bool AllowBypass;
        public string[] AllowedTools;
        public string[] AddDirs;
        public Dictionary<string, string> Env = new Dictionary<string, string>();
    }

    /// <summary>
    /// One live Claude Code process in headless streaming mode
    /// (-p --input-format stream-json --output-format stream-json). Permission prompts,
    /// interrupts and mode/model changes go through the control channel the official Agent SDK uses
    /// (--permission-prompt-tool stdio, control_request / control_response).
    /// </summary>
    public sealed class ClaudeSession : IDisposable
    {
        Process process;
        StreamWriter stdin;
        JobObject job;
        readonly object writeGate = new object();
        readonly StringBuilder stderrTail = new StringBuilder();
        int requestCounter;
        volatile bool disposed;

        public string SessionId { get; private set; }
        public string Model { get; private set; }
        public string Mode { get; private set; }
        public bool Busy { get; private set; }
        public bool Running => process != null && !process.HasExited;

        /// <summary>Every stdout message: raw JSON line plus its parsed form.</summary>
        public event Action<string, Dictionary<string, object>> Message;
        /// <summary>A can_use_tool request: request id and the request object (tool_name, input, permission_suggestions, tool_use_id).</summary>
        public event Action<string, Dictionary<string, object>> PermissionRequest;
        public event Action<int, string> Exited;

        public void Start(ClaudeSessionOptions o)
        {
            var args = new List<string>
            {
                "-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose",
                "--include-partial-messages", "--permission-prompt-tool", "stdio",
                "--permission-mode", string.IsNullOrEmpty(o.Mode) ? "default" : o.Mode,
            };
            if (!string.IsNullOrEmpty(o.Model) && o.Model != "default") { args.Add("--model"); args.Add(o.Model); }
            if (!string.IsNullOrEmpty(o.Effort)) { args.Add("--effort"); args.Add(o.Effort); }
            if (!string.IsNullOrEmpty(o.McpConfigPath)) { args.Add("--mcp-config"); args.Add(o.McpConfigPath); }
            if (!string.IsNullOrEmpty(o.SystemPromptPath)) { args.Add("--append-system-prompt-file"); args.Add(o.SystemPromptPath); }
            if (!string.IsNullOrEmpty(o.PluginDir) && Directory.Exists(o.PluginDir)) { args.Add("--plugin-dir"); args.Add(o.PluginDir); }
            if (o.AllowedTools != null && o.AllowedTools.Length > 0) { args.Add("--allowedTools"); args.Add(string.Join(",", o.AllowedTools)); }
            if (o.AddDirs != null) foreach (var d in o.AddDirs) { args.Add("--add-dir"); args.Add(d); }
            if (o.AllowBypass) args.Add("--allow-dangerously-skip-permissions");
            if (!string.IsNullOrEmpty(o.ResumeId)) { args.Add("--resume"); args.Add(o.ResumeId); SessionId = o.ResumeId; }
            else if (!string.IsNullOrEmpty(o.NewSessionId)) { args.Add("--session-id"); args.Add(o.NewSessionId); SessionId = o.NewSessionId; }

            var psi = new ProcessStartInfo(o.Exe, ProcessUtil.Join(args))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false),
                WorkingDirectory = o.WorkDir,
            };
            ProcessUtil.ApplyEnv(psi.EnvironmentVariables, o.Env);
            Log.Info("claude start: " + psi.FileName + " " + psi.Arguments + "  (cwd " + o.WorkDir + ")");

            Model = o.Model;
            Mode = o.Mode;
            process = Process.Start(psi);
            job = new JobObject();
            job.Assign(process.Handle);
            stdin = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                lock (stderrTail)
                {
                    stderrTail.AppendLine(e.Data);
                    if (stderrTail.Length > 6000) stderrTail.Remove(0, stderrTail.Length - 4000);
                }
            };
            process.BeginErrorReadLine();
            new Thread(ReadLoop) { IsBackground = true, Name = "claude-stdout" }.Start();
            Control(Json.O("subtype", "initialize", "hooks", null));
        }

        void ReadLoop()
        {
            var p = process;
            try
            {
                string line;
                while ((line = p.StandardOutput.ReadLine()) != null)
                {
                    if (line.Length == 0 || line[0] != '{') continue;
                    var m = Json.ParseObject(line);
                    if (m == null) continue;
                    var type = m.Str("type");
                    if (type == "control_request")
                    {
                        var req = m.Obj("request");
                        var id = m.Str("request_id");
                        if (req.Str("subtype") == "can_use_tool") PermissionRequest?.Invoke(id, req);
                        else Write(Json.O("type", "control_response", "response",
                            Json.O("subtype", "error", "request_id", id, "error", "Not supported by CAD Master")));
                        continue;
                    }
                    if (type == "system" && m.Str("subtype") == "init")
                    {
                        SessionId = m.Str("session_id") ?? SessionId;
                        Model = m.Str("model") ?? Model;
                        Mode = m.Str("permissionMode") ?? Mode;
                    }
                    else if (type == "system" && m.Str("subtype") == "status" && m.ContainsKey("permissionMode"))
                    {
                        Mode = m.Str("permissionMode") ?? Mode;
                    }
                    else if (type == "result")
                    {
                        Busy = false;
                        SessionId = m.Str("session_id") ?? SessionId;
                    }
                    try { Message?.Invoke(line, m); } catch (Exception ex) { Log.Error("message handler", ex); }
                }
            }
            catch (Exception ex) { if (!disposed) Log.Error("claude read loop", ex); }

            int code = -1;
            try { p.WaitForExit(3000); code = p.ExitCode; } catch { }
            Busy = false;
            string tail;
            lock (stderrTail) tail = stderrTail.ToString();
            Log.Info("claude exited " + code + (tail.Length > 0 ? ": " + tail : ""));
            if (!disposed) Exited?.Invoke(code, tail);
        }

        public void SendUser(object[] content)
        {
            Busy = true;
            Write(Json.O("type", "user",
                "message", Json.O("role", "user", "content", content),
                "parent_tool_use_id", null, "session_id", SessionId ?? "default"));
        }

        public void Control(Dictionary<string, object> request)
        {
            Write(Json.O("type", "control_request", "request_id", "cm_" + Interlocked.Increment(ref requestCounter), "request", request));
        }

        public void Interrupt() => Control(Json.O("subtype", "interrupt"));

        public void SetMode(string mode) { Mode = mode; Control(Json.O("subtype", "set_permission_mode", "mode", mode)); }

        public void SetModel(string model) { Model = model; Control(Json.O("subtype", "set_model", "model", model == "default" ? null : model)); }

        public void RespondPermission(string requestId, Dictionary<string, object> response)
        {
            Write(Json.O("type", "control_response", "response", Json.O("subtype", "success", "request_id", requestId, "response", response)));
        }

        void Write(Dictionary<string, object> message)
        {
            lock (writeGate)
            {
                if (stdin == null || process == null || process.HasExited) throw new InvalidOperationException("Claude Code is not running.");
                stdin.WriteLine(Json.Serialize(message));
            }
        }

        /// <summary>Kills Claude Code and everything it started (scripts, shells).</summary>
        public void Kill()
        {
            try { job?.Terminate(); } catch { }
            try { if (process != null && !process.HasExited) process.Kill(); } catch { }
        }

        public void Dispose()
        {
            disposed = true;
            try { stdin?.Close(); } catch { }
            Kill();
            job?.Dispose();
            process?.Dispose();
        }
    }
}
