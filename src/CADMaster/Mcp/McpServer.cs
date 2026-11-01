using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace CADMaster
{
    /// <summary>
    /// Minimal MCP server (Streamable HTTP, JSON responses) on localhost with a per-session bearer token.
    /// Claude Code connects to it through --mcp-config; tool calls are handed to a callback.
    /// </summary>
    public sealed class McpServer : IDisposable
    {
        readonly HttpListener listener = new HttpListener();
        readonly Func<string, Dictionary<string, object>, Dictionary<string, object>> callTool;
        readonly Func<List<Dictionary<string, object>>> listTools;
        volatile bool stopped;

        public int Port { get; private set; }
        public string Token { get; } = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        public string Url => "http://localhost:" + Port + "/mcp";

        public McpServer(Func<List<Dictionary<string, object>>> listTools, Func<string, Dictionary<string, object>, Dictionary<string, object>> callTool)
        {
            this.listTools = listTools;
            this.callTool = callTool;
        }

        public void Start()
        {
            for (int attempt = 0; ; attempt++)
            {
                Port = FreePort();
                listener.Prefixes.Clear();
                listener.Prefixes.Add("http://localhost:" + Port + "/");
                try { listener.Start(); break; }
                catch (HttpListenerException) when (attempt < 5) { }
            }
            new Thread(AcceptLoop) { IsBackground = true, Name = "mcp-accept" }.Start();
            Log.Info("MCP server on " + Url);
        }

        static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        /// <summary>Writes the --mcp-config file for this server.</summary>
        public string WriteConfig(string path)
        {
            var cfg = Json.O("mcpServers", Json.O("solidworks", Json.O("type", "http", "url", Url,
                "headers", Json.O("Authorization", "Bearer " + Token))));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, Json.Serialize(cfg));
            return path;
        }

        void AcceptLoop()
        {
            while (!stopped)
            {
                HttpListenerContext ctx;
                try { ctx = listener.GetContext(); }
                catch { if (stopped) return; continue; }
                ThreadPool.QueueUserWorkItem(_ => Handle(ctx));
            }
        }

        void Handle(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;
            try
            {
                if (req.Headers["Authorization"] != "Bearer " + Token) { res.StatusCode = 401; return; }
                if (req.HttpMethod == "GET") { res.StatusCode = 405; return; }
                if (req.HttpMethod == "DELETE") { res.StatusCode = 200; return; }
                if (req.HttpMethod != "POST") { res.StatusCode = 405; return; }

                string body;
                using (var r = new StreamReader(req.InputStream, Encoding.UTF8)) body = r.ReadToEnd();
                var parsed = Json.Parse(body);
                object reply;
                if (parsed is object[] batch)
                {
                    var replies = new List<object>();
                    foreach (var m in batch) { var r = Dispatch(m as Dictionary<string, object>); if (r != null) replies.Add(r); }
                    reply = replies.Count > 0 ? replies : null;
                }
                else reply = Dispatch(parsed as Dictionary<string, object>);

                if (reply == null) { res.StatusCode = 202; return; }
                var bytes = Encoding.UTF8.GetBytes(Json.Serialize(reply));
                res.StatusCode = 200;
                res.ContentType = "application/json";
                res.ContentLength64 = bytes.Length;
                res.OutputStream.Write(bytes, 0, bytes.Length);
            }
            catch (Exception ex)
            {
                Log.Error("mcp request", ex);
                try { res.StatusCode = 500; } catch { }
            }
            finally { try { res.Close(); } catch { } }
        }

        Dictionary<string, object> Dispatch(Dictionary<string, object> msg)
        {
            if (msg == null) return null;
            msg.TryGetValue("id", out var id);
            var method = msg.Str("method");
            if (id == null) return null; // notification
            try
            {
                switch (method)
                {
                    case "initialize":
                        return Ok(id, Json.O(
                            "protocolVersion", msg.Obj("params").Str("protocolVersion", "2025-06-18"),
                            "capabilities", Json.O("tools", Json.O("listChanged", false)),
                            "serverInfo", Json.O("name", "cadmaster-solidworks", "version", Controller.Version),
                            "instructions", "Live access to the SOLIDWORKS session that hosts the CAD Master panel."));
                    case "ping": return Ok(id, new Dictionary<string, object>());
                    case "tools/list": return Ok(id, Json.O("tools", listTools()));
                    case "tools/call":
                        var p = msg.Obj("params");
                        var name = p.Str("name");
                        var args = p.Obj("arguments") ?? new Dictionary<string, object>();
                        try { return Ok(id, callTool(name, args)); }
                        catch (Exception ex)
                        {
                            var inner = ex is System.Reflection.TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                            Log.Error("tool " + name, inner);
                            return Ok(id, Json.O("isError", true, "content", new object[] { Json.O("type", "text", "text", name + " failed: " + inner.Message) }));
                        }
                    default:
                        return Json.O("jsonrpc", "2.0", "id", id, "error", Json.O("code", -32601, "message", "Method not found: " + method));
                }
            }
            catch (Exception ex)
            {
                return Json.O("jsonrpc", "2.0", "id", id, "error", Json.O("code", -32603, "message", ex.Message));
            }
        }

        static Dictionary<string, object> Ok(object id, object result) => Json.O("jsonrpc", "2.0", "id", id, "result", result);

        public void Dispose()
        {
            stopped = true;
            try { listener.Stop(); listener.Close(); } catch { }
        }
    }
}
