using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace CADMaster
{
    /// <summary>A release, from its update.json (written by installer\release.ps1).</summary>
    public sealed class UpdateInfo
    {
        public string Version, Released, MinSolidworks, Notes, PageUrl;
        public bool Critical;
        public string[] Highlights = new string[0];
        public string ZipName, ZipUrl, ZipSha256;
        public long ZipSize;
        /// <summary>Why this PC can't take the update (for example an older SOLIDWORKS), or null.</summary>
        public string Blocked;

        public Dictionary<string, object> ToJson() => Json.O("version", Version, "released", Released, "critical", Critical,
            "minSolidworks", MinSolidworks, "highlights", Highlights, "notes", Notes, "page", PageUrl, "size", ZipSize, "blocked", Blocked);

        public static UpdateInfo FromManifest(Dictionary<string, object> m, Func<string, string> urlOf, string page)
        {
            var zip = m.Obj("zip");
            var u = new UpdateInfo
            {
                Version = m.Str("version"), Released = m.Str("released"), MinSolidworks = m.Str("minSolidworks"),
                Notes = m.Str("notes", ""), Critical = m.Bool("critical"), PageUrl = page,
                Highlights = (m.Arr("highlights") ?? new object[0]).OfType<string>().ToArray(),
                ZipName = zip.Str("name"), ZipSha256 = zip.Str("sha256", "").ToLowerInvariant(), ZipSize = (long)zip.Dbl("size"),
            };
            if (string.IsNullOrEmpty(u.Version) || string.IsNullOrEmpty(u.ZipName) || u.ZipSha256.Length != 64)
                throw new InvalidDataException("update.json is incomplete");
            u.ZipUrl = urlOf(u.ZipName);
            return u;
        }
    }

    /// <summary>
    /// In-app updates. The installer puts each version in its own folder,
    /// %LOCALAPPDATA%\Programs\CAD Master\app-&lt;version&gt;, and points the per-user COM class at it.
    /// An update downloads the release zip, checks its SHA-256, unpacks it next to the running version
    /// (whose DLL SOLIDWORKS keeps locked) and switches the HKCU registration; SOLIDWORKS loads it at
    /// its next start. The version before is kept for rolling back. Nothing here needs admin.
    /// </summary>
    public static class Updater
    {
        public const string Repo = "Nilove2018/cad-master-solidworks";
        const string InfoKey = @"Software\CADMaster";
        const string ClsidKey = @"Software\Classes\CLSID\{C4D7A1E2-5B39-4F6A-9E1D-2A8B7C3F5D10}\InprocServer32";
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{8B1F6C2E-6A2D-4C1B-9B7E-0CADA57E5001}_is1";

        static string UpdatesDir => Path.Combine(Paths.DataDir, "updates");
        static string InstalledManifest => Path.Combine(UpdatesDir, "installed.json");

        static string Reg(string name)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(InfoKey)) return k?.GetValue(name) as string;
        }

        /// <summary>The installer's folder, or null for a developer build (bin, registered by install.ps1).</summary>
        public static string InstallDir
        {
            get
            {
                var dir = Reg("InstallDir");
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
                var full = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
                return Path.GetFullPath(Paths.AddinDir).StartsWith(full, StringComparison.OrdinalIgnoreCase) ? full.TrimEnd('\\') : null;
            }
        }

        public static bool Installed => InstallDir != null;

        /// <summary>The version the registration points at; differs from the running one after an update, until SOLIDWORKS restarts.</summary>
        public static string RegisteredVersion => Installed ? Reg("Version") : null;
        public static string PreviousVersion => Installed && Directory.Exists(Reg("PreviousAppDir") ?? "") ? Reg("PreviousVersion") : null;

        // ------------------------------------------------------------------ check
        /// <summary>
        /// The newest release that is newer than the running version, or null. Pre-releases count when
        /// asked for, or while the running version is one itself (0.x, or 1.2.0-beta.1).
        /// CADMASTER_UPDATE_FEED (an update.json URL or path) replaces GitHub, for testing.
        /// </summary>
        public static UpdateInfo Check(bool includePrereleases)
        {
            var feed = Environment.GetEnvironmentVariable("CADMASTER_UPDATE_FEED");
            UpdateInfo best = null;
            if (!string.IsNullOrEmpty(feed))
            {
                var baseUri = new Uri(feed.Contains("://") ? feed : Path.GetFullPath(feed));
                best = UpdateInfo.FromManifest(Json.ParseObject(GetText(baseUri.AbsoluteUri)), n => new Uri(baseUri, n).AbsoluteUri, null);
            }
            else
            {
                bool pre = includePrereleases || IsPrerelease(Controller.Version);
                var list = Json.Parse(GetText("https://api.github.com/repos/" + Repo + "/releases?per_page=20")) as object[] ?? new object[0];
                foreach (var r in list.OfType<Dictionary<string, object>>())
                {
                    if (r.Bool("draft") || (r.Bool("prerelease") && !pre)) continue;
                    var assets = (r.Arr("assets") ?? new object[0]).OfType<Dictionary<string, object>>().ToList();
                    var manifest = assets.FirstOrDefault(a => a.Str("name") == "update.json");
                    if (manifest == null) continue;
                    var tag = (r.Str("tag_name") ?? "").TrimStart('v');
                    if (best != null && Compare(tag, best.Version) <= 0) continue;
                    if (Compare(tag, Controller.Version) <= 0) continue;
                    try
                    {
                        best = UpdateInfo.FromManifest(Json.ParseObject(GetText(manifest.Str("browser_download_url"))),
                            n => assets.FirstOrDefault(a => a.Str("name") == n)?.Str("browser_download_url") ?? throw new InvalidDataException(n + " is missing from the release"),
                            r.Str("html_url"));
                    }
                    catch (Exception ex) { Log.Error("release " + tag, ex); }
                }
            }
            return best != null && Compare(best.Version, Controller.Version) > 0 ? best : null;
        }

        static bool IsPrerelease(string v) => v.Contains("-") || v.StartsWith("0.");

        /// <summary>Semantic version order: 1.2.0 &gt; 1.2.0-beta.2 &gt; 1.2.0-beta.1 &gt; 1.1.9.</summary>
        public static int Compare(string a, string b)
        {
            string[] Split(string v, out string pre) { var i = (v ?? "0").IndexOf('-'); pre = i < 0 ? null : v.Substring(i + 1); return (i < 0 ? v ?? "0" : v.Substring(0, i)).Split('.'); }
            var x = Split(a, out var pa); var y = Split(b, out var pb);
            for (int i = 0; i < 3; i++)
            {
                int.TryParse(i < x.Length ? x[i] : "0", out var xi);
                int.TryParse(i < y.Length ? y[i] : "0", out var yi);
                if (xi != yi) return xi.CompareTo(yi);
            }
            if (pa == pb) return 0;
            if (pa == null) return 1;
            if (pb == null) return -1;
            var px = pa.Split('.'); var py = pb.Split('.');
            for (int i = 0; i < Math.Max(px.Length, py.Length); i++)
            {
                if (i >= px.Length) return -1;
                if (i >= py.Length) return 1;
                bool nx = int.TryParse(px[i], out var ix), ny = int.TryParse(py[i], out var iy);
                int c = nx && ny ? ix.CompareTo(iy) : nx ? -1 : ny ? 1 : string.CompareOrdinal(px[i], py[i]);
                if (c != 0) return c;
            }
            return 0;
        }

        // ------------------------------------------------------------------ install
        /// <summary>Downloads, verifies and unpacks the update, then points the registration at it.</summary>
        public static void Install(UpdateInfo u, Action<string, int> progress)
        {
            var root = InstallDir ?? throw new InvalidOperationException("This is a developer build. Update it with git pull and build.ps1.");
            if (u.Blocked != null) throw new InvalidOperationException(u.Blocked);
            Directory.CreateDirectory(UpdatesDir);
            var zip = Path.Combine(UpdatesDir, u.ZipName);

            progress("download", 0);
            Download(u.ZipUrl, zip, u.ZipSize, pct => progress("download", pct));
            progress("verify", 100);
            var info = new FileInfo(zip);
            if (u.ZipSize > 0 && info.Length != u.ZipSize) { File.Delete(zip); throw new InvalidDataException("The download is incomplete (" + info.Length + " of " + u.ZipSize + " bytes). Try again."); }
            string hash;
            using (var sha = SHA256.Create()) using (var s = File.OpenRead(zip)) hash = string.Concat(sha.ComputeHash(s).Select(b => b.ToString("x2")));
            if (hash != u.ZipSha256) { File.Delete(zip); throw new InvalidDataException("The download doesn't match the release's checksum, so it wasn't installed. Try again later."); }

            progress("unpack", 100);
            var target = Path.Combine(root, "app-" + u.Version);
            if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(Paths.AddinDir), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("CAD Master " + u.Version + " is already running.");
            var partial = target + ".partial";
            if (Directory.Exists(partial)) Directory.Delete(partial, true);
            ZipFile.ExtractToDirectory(zip, partial); // refuses entries that point outside the folder
            var dll = Path.Combine(partial, "CADMaster.dll");
            if (!File.Exists(dll)) throw new InvalidDataException("The update has no CADMaster.dll.");
            var dllVersion = FileVersionInfo.GetVersionInfo(dll).ProductVersion;
            if (dllVersion != u.Version) throw new InvalidDataException("The update contains version " + dllVersion + ", not " + u.Version + ".");
            if (Directory.Exists(target)) Directory.Delete(target, true); // an earlier update attempt, or a rolled-back version
            Directory.Move(partial, target);

            progress("switch", 100);
            var current = Path.GetFullPath(Paths.AddinDir);
            SwitchTo(target, u.Version, current, Controller.Version);
            File.WriteAllText(InstalledManifest, Json.Serialize(u.ToJson()));
            try { File.Delete(zip); } catch { }
            Log.Info("update: installed " + u.Version + " in " + target + "; loads at the next SOLIDWORKS start");
        }

        /// <summary>Points the registration back at the version kept before the last update.</summary>
        public static string RollBack()
        {
            if (!Installed) throw new InvalidOperationException("This is a developer build.");
            var prevDir = Reg("PreviousAppDir");
            var prev = Reg("PreviousVersion");
            if (string.IsNullOrEmpty(prevDir) || !File.Exists(Path.Combine(prevDir, "CADMaster.dll"))) throw new InvalidOperationException("No earlier version is kept on this PC.");
            SwitchTo(prevDir, prev, Reg("AppDir"), Reg("Version"));
            Log.Info("update: rolled back to " + prev);
            return prev;
        }

        /// <summary>Points the per-user COM class at appDir\CADMaster.dll and records the version it replaces.</summary>
        static void SwitchTo(string appDir, string version, string previousDir, string previousVersion)
        {
            var codeBase = "file:///" + Path.Combine(appDir, "CADMaster.dll").Replace('\\', '/');
            foreach (var key in new[] { ClsidKey, ClsidKey + @"\1.0.0.0" })
                using (var k = Registry.CurrentUser.CreateSubKey(key)) k.SetValue("CodeBase", codeBase);
            using (var k = Registry.CurrentUser.CreateSubKey(InfoKey))
            {
                k.SetValue("AppDir", appDir);
                k.SetValue("Version", version);
                k.SetValue("PreviousAppDir", previousDir ?? "");
                k.SetValue("PreviousVersion", previousVersion ?? "");
            }
            // Apps & features shows the version from the installer's uninstall entry
            using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey, true)) k?.SetValue("DisplayVersion", version);
        }

        /// <summary>Deletes app-* folders other than the running, registered and previous versions. Locked ones wait for next time.</summary>
        public static void Cleanup()
        {
            var root = InstallDir;
            if (root == null) return;
            var keep = new[] { Paths.AddinDir, Reg("AppDir"), Reg("PreviousAppDir") }
                .Where(p => !string.IsNullOrEmpty(p)).Select(p => Path.GetFullPath(p).TrimEnd('\\')).ToList();
            foreach (var dir in Directory.GetDirectories(root, "app-*"))
            {
                if (keep.Any(k => string.Equals(k, dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))) continue;
                try { Directory.Delete(dir, true); Log.Info("update: removed old " + Path.GetFileName(dir)); }
                catch (Exception ex) { Log.Info("update: " + Path.GetFileName(dir) + " not removed yet (" + ex.Message + ")"); }
            }
        }

        /// <summary>The notes of the version now running, if it was installed by the updater.</summary>
        public static Dictionary<string, object> InstalledNotes(string version)
        {
            try
            {
                var m = File.Exists(InstalledManifest) ? Json.ParseObject(File.ReadAllText(InstalledManifest)) : null;
                return m != null && m.Str("version") == version ? m : null;
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------ HTTP
        static Updater() { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }

        static HttpWebRequest Request(string url, int timeoutMs)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "CAD-Master/" + Controller.Version;
            req.Accept = url.Contains("api.github.com") ? "application/vnd.github+json" : "*/*";
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            return req;
        }

        static string GetText(string url)
        {
            if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return File.ReadAllText(new Uri(url).LocalPath);
            try
            {
                using (var resp = (HttpWebResponse)Request(url, 20000).GetResponse())
                using (var r = new StreamReader(resp.GetResponseStream())) return r.ReadToEnd();
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse hr && (int)hr.StatusCode == 403)
            {
                throw new WebException("GitHub's hourly limit for update checks is used up on this network. Try again later.", ex);
            }
        }

        static void Download(string url, string path, long expected, Action<int> progress)
        {
            var part = path + ".part";
            if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) { File.Copy(new Uri(url).LocalPath, part, true); }
            else
            {
                using (var resp = Request(url, 30000).GetResponse())
                using (var input = resp.GetResponseStream())
                using (var output = File.Create(part))
                {
                    long total = expected > 0 ? expected : resp.ContentLength, done = 0;
                    var buf = new byte[81920];
                    int n, last = -1;
                    while ((n = input.Read(buf, 0, buf.Length)) > 0)
                    {
                        output.Write(buf, 0, n);
                        done += n;
                        var pct = total > 0 ? (int)Math.Min(100, done * 100 / total) : 0;
                        if (pct / 5 != last / 5) { last = pct; progress(pct); }
                    }
                }
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(part, path);
        }
    }
}
