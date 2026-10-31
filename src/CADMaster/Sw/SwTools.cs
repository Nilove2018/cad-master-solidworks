using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CADMaster
{
    /// <summary>
    /// The SOLIDWORKS tools Claude gets through the CAD Master MCP server. Every method must run on the
    /// SOLIDWORKS main thread (the controller marshals the calls). Lengths are reported in mm.
    /// </summary>
    public class SwTools
    {
        readonly Func<ISldWorks> app;
        const double MM = 1000.0;

        public SwTools(Func<ISldWorks> app) { this.app = app; }

        public static readonly string[] ReadOnlyTools =
            { "sw_status", "sw_selection", "sw_feature_tree", "sw_screenshot", "sw_check", "sw_mass_properties", "sw_custom_properties" };

        public static List<Dictionary<string, object>> Definitions()
        {
            Dictionary<string, object> Schema(params object[] props)
            {
                var p = new Dictionary<string, object>();
                for (int i = 0; i + 2 < props.Length + 1; i += 3)
                    p[(string)props[i]] = Json.O("type", props[i + 1], "description", props[i + 2]);
                return Json.O("type", "object", "properties", p);
            }
            var tools = new List<Dictionary<string, object>>
            {
                Json.O("name", "sw_status", "description",
                    "SOLIDWORKS version, the active document (path, type, active configuration, units, unsaved changes) and all open documents. Call this first to see what the user is working on.",
                    "inputSchema", Schema()),
                Json.O("name", "sw_selection", "description",
                    "What the user has selected in SOLIDWORKS right now: faces (area mm², plane normal or cylinder radius), edges (length, circle diameter/centre), vertices, features, components, planes. Coordinates in mm, model frame.",
                    "inputSchema", Schema()),
                Json.O("name", "sw_feature_tree", "description",
                    "Feature tree of the active document: features with type, suppression and rebuild errors. For assemblies also the top-level components (path, configuration, fixed, constraint status) and mate errors.",
                    "inputSchema", Schema("max_items", "integer", "Maximum features to list (default 200).")),
                Json.O("name", "sw_screenshot", "description",
                    "Image of the active SOLIDWORKS viewport so you can see the model. view: current (default), isometric, trimetric, dimetric, front, back, top, bottom, left, right. Other views are restored to the user's view afterwards.",
                    "inputSchema", Schema("view", "string", "Named view or 'current'.", "zoom_to_fit", "boolean", "Zoom to fit before capturing (default true for named views).", "width", "integer", "Image width in px (default 1280).")),
                Json.O("name", "sw_check", "description",
                    "Quick verification of the active document (skill section 12): optional rebuild, What's Wrong count, features with errors, solid body count, volume, under-defined sketches.",
                    "inputSchema", Schema("rebuild", "boolean", "Run ForceRebuild3 first (default false).")),
                Json.O("name", "sw_mass_properties", "description",
                    "Mass (g), volume (mm³), surface area (mm²), centre of mass (mm) and material of the active document.",
                    "inputSchema", Schema()),
                Json.O("name", "sw_custom_properties", "description",
                    "Custom properties of the active document (file level and active configuration).",
                    "inputSchema", Schema()),
                Json.O("name", "sw_run_macro", "description",
                    "Run a SOLIDWORKS VBA macro (.swp or .swb) inside SOLIDWORKS with ISldWorks.RunMacro2. Use for fast in-process automation; prefer the skill's Python/swlib route for builds.",
                    "inputSchema", Json.O("type", "object", "required", new[] { "path" }, "properties", Json.O(
                        "path", Json.O("type", "string", "description", "Full path of the macro file."),
                        "module", Json.O("type", "string", "description", "Module name (default: file name without extension for .swb, 'Module1' otherwise)."),
                        "procedure", Json.O("type", "string", "description", "Procedure to run (default 'main').")))),
            };
            // Plan mode lets Claude Code run only tools marked read-only without asking.
            foreach (var t in tools)
                t["annotations"] = ReadOnlyTools.Contains((string)t["name"])
                    ? Json.O("readOnlyHint", true, "openWorldHint", false)
                    : Json.O("readOnlyHint", false, "destructiveHint", true, "openWorldHint", false);
            return tools;
        }

        /// <summary>Runs a tool and returns MCP content blocks.</summary>
        public Dictionary<string, object> Call(string name, Dictionary<string, object> a)
        {
            switch (name)
            {
                case "sw_status": return Text(Status());
                case "sw_selection": return Text(Selection());
                case "sw_feature_tree": return Text(FeatureTree(a.Int("max_items", 200)));
                case "sw_check": return Text(Check(a.Bool("rebuild")));
                case "sw_mass_properties": return Text(MassProperties());
                case "sw_custom_properties": return Text(CustomProperties());
                case "sw_run_macro": return Text(RunMacro(a.Str("path"), a.Str("module"), a.Str("procedure")));
                case "sw_screenshot":
                    var view = a.Str("view", "current");
                    var png = Screenshot(view, a.Bool("zoom_to_fit", view != "current"), a.Int("width", 1280));
                    return Json.O("content", new object[]
                    {
                        Json.O("type", "image", "data", Convert.ToBase64String(png), "mimeType", "image/png"),
                        Json.O("type", "text", "text", "Viewport of " + DocTitle() + " (" + view + " view)."),
                    });
                default: throw new ArgumentException("Unknown tool " + name);
            }
        }

        static Dictionary<string, object> Text(object value) =>
            Json.O("content", new object[] { Json.O("type", "text", "text", value as string ?? Json.Serialize(value)) });

        ModelDoc2 Doc()
        {
            var d = app().ActiveDoc as ModelDoc2;
            if (d == null) throw new InvalidOperationException("No document is open in SOLIDWORKS.");
            return d;
        }

        string DocTitle() { var d = app().ActiveDoc as ModelDoc2; return d == null ? "(no document)" : d.GetTitle(); }

        static string DocType(int t) => t == (int)swDocumentTypes_e.swDocPART ? "part" : t == (int)swDocumentTypes_e.swDocASSEMBLY ? "assembly" : t == (int)swDocumentTypes_e.swDocDRAWING ? "drawing" : "other";

        static double R(double v, int digits = 3) => Math.Round(v, digits);
        static double[] Mm(object arr, int count = 3) => arr is double[] d ? d.Take(count).Select(v => R(v * MM)).ToArray() : null;

        // ------------------------------------------------------------------ status
        public Dictionary<string, object> Status()
        {
            var sw = app();
            var result = Json.O("solidworks", "SOLIDWORKS " + sw.RevisionNumber(), "processId", System.Diagnostics.Process.GetCurrentProcess().Id);
            var doc = sw.ActiveDoc as ModelDoc2;
            if (doc != null) result["activeDocument"] = DocInfo(doc, true);
            var docs = new List<object>();
            if (sw.GetDocuments() is object[] all)
                foreach (var o in all)
                    if (o is ModelDoc2 d && d.Visible) docs.Add(DocInfo(d, false));
            result["openDocuments"] = docs;
            return result;
        }

        Dictionary<string, object> DocInfo(ModelDoc2 doc, bool detail)
        {
            var info = Json.O("title", doc.GetTitle(), "path", doc.GetPathName(), "type", DocType(doc.GetType()));
            if (!detail) return info;
            info["unsavedChanges"] = doc.GetSaveFlag();
            try { info["activeConfiguration"] = doc.ConfigurationManager.ActiveConfiguration.Name; } catch { }
            int units = doc.Extension.GetUserPreferenceInteger((int)swUserPreferenceIntegerValue_e.swUnitSystem, 0);
            info["units"] = units == 1 ? "CGS" : units == 2 ? "MKS" : units == 3 ? "IPS" : units == 5 ? "MMGS" : "Custom";
            var sel = (SelectionMgr)doc.SelectionManager;
            info["selectedCount"] = sel?.GetSelectedObjectCount2(-1) ?? 0;
            return info;
        }

        // ------------------------------------------------------------------ selection
        public Dictionary<string, object> Selection()
        {
            var doc = Doc();
            var sel = (SelectionMgr)doc.SelectionManager;
            int n = sel.GetSelectedObjectCount2(-1);
            var items = new List<object>();
            for (int i = 1; i <= n; i++)
            {
                int type = sel.GetSelectedObjectType3(i, -1);
                object obj = sel.GetSelectedObject6(i, -1);
                var item = Json.O("index", i, "type", SelectTypeName(type));
                try { DescribeEntity(item, type, obj); } catch (Exception ex) { item["error"] = ex.Message; }
                try
                {
                    if (sel.GetSelectedObjectsComponent4(i, -1) is Component2 comp)
                        item["component"] = comp.Name2;
                }
                catch { }
                try { var pt = Mm(sel.GetSelectionPoint2(i, -1)); if (pt != null) item["pickPoint_mm"] = pt; } catch { }
                items.Add(item);
            }
            return Json.O("document", doc.GetTitle(), "count", n, "items", items);
        }

        static string SelectTypeName(int t)
        {
            switch ((swSelectType_e)t)
            {
                case swSelectType_e.swSelFACES: return "face";
                case swSelectType_e.swSelEDGES: return "edge";
                case swSelectType_e.swSelVERTICES: return "vertex";
                case swSelectType_e.swSelDATUMPLANES: return "plane";
                case swSelectType_e.swSelDATUMAXES: return "axis";
                case swSelectType_e.swSelDATUMPOINTS: return "point";
                case swSelectType_e.swSelSKETCHES: return "sketch";
                case swSelectType_e.swSelSKETCHSEGS: return "sketch segment";
                case swSelectType_e.swSelSKETCHPOINTS: return "sketch point";
                case swSelectType_e.swSelCOMPONENTS: return "component";
                case swSelectType_e.swSelMATES: return "mate";
                case swSelectType_e.swSelBODYFEATURES: return "feature";
                case swSelectType_e.swSelSOLIDBODIES: return "solid body";
                case swSelectType_e.swSelDIMENSIONS: return "dimension";
                default: return "type " + t;
            }
        }

        static void DescribeEntity(Dictionary<string, object> item, int type, object obj)
        {
            if (obj is Face2 face)
            {
                item["area_mm2"] = R(face.GetArea() * MM * MM, 2);
                var surf = (Surface)face.GetSurface();
                if (surf.IsPlane()) { item["surface"] = "plane"; item["normal"] = ((double[])face.Normal).Select(v => R(v, 4)).ToArray(); }
                else if (surf.IsCylinder())
                {
                    var c = (double[])surf.CylinderParams;
                    item["surface"] = "cylinder";
                    item["diameter_mm"] = R(c[6] * 2 * MM);
                    item["axisPoint_mm"] = Mm(c);
                    item["axisDir"] = new[] { R(c[3], 4), R(c[4], 4), R(c[5], 4) };
                }
                else if (surf.IsCone()) item["surface"] = "cone";
                else if (surf.IsSphere()) item["surface"] = "sphere";
                else if (surf.IsTorus()) item["surface"] = "torus";
                else item["surface"] = "freeform";
                var box = face.GetBox() as double[];
                if (box != null) item["box_mm"] = box.Select(v => R(v * MM)).ToArray();
                if (face.GetFeature() is Feature f) item["feature"] = f.Name;
            }
            else if (obj is Edge edge)
            {
                var curve = (Curve)edge.GetCurve();
                var cp = (CurveParamData)edge.GetCurveParams3();
                item["length_mm"] = R(curve.GetLength3(cp.UMinValue, cp.UMaxValue) * MM);
                item["start_mm"] = Mm(cp.StartPoint);
                item["end_mm"] = Mm(cp.EndPoint);
                if (curve.IsCircle())
                {
                    var c = (double[])curve.CircleParams;
                    item["curve"] = "circle/arc";
                    item["diameter_mm"] = R(c[6] * 2 * MM);
                    item["center_mm"] = Mm(c);
                }
                else if (curve.IsLine()) item["curve"] = "line";
                else item["curve"] = "curve";
            }
            else if (obj is Vertex v) item["point_mm"] = Mm(v.GetPoint());
            else if (obj is Component2 comp)
            {
                item["name"] = comp.Name2;
                item["path"] = comp.GetPathName();
                item["configuration"] = comp.ReferencedConfiguration;
            }
            else if (obj is Feature feat) { item["name"] = feat.Name; item["featureType"] = feat.GetTypeName2(); }
            else if (obj is Body2 body) item["name"] = body.Name;
        }

        /// <summary>Plain-text summary of the selection, attached to a chat message.</summary>
        public string SelectionSummary()
        {
            var s = Selection();
            var sb = new StringBuilder();
            sb.AppendLine("Selection in " + s["document"] + " (" + s["count"] + " item(s)):");
            foreach (Dictionary<string, object> it in (List<object>)s["items"])
                sb.AppendLine("- " + string.Join(", ", it.Where(kv => kv.Key != "index").Select(kv => kv.Key + ": " + Fmt(kv.Value))));
            return sb.ToString();
        }

        static string Fmt(object v) => v is double[] a ? "(" + string.Join(", ", a.Select(x => x.ToString(CultureInfo.InvariantCulture))) + ")"
            : Convert.ToString(v, CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ feature tree
        public Dictionary<string, object> FeatureTree(int max)
        {
            var doc = Doc();
            var features = new List<object>();
            int total = 0;
            for (var f = doc.FirstFeature() as Feature; f != null; f = f.GetNextFeature() as Feature)
            {
                total++;
                if (features.Count >= max) continue;
                var item = Json.O("name", f.Name, "type", f.GetTypeName2());
                try { if (f.IsSuppressed2((int)swInConfigurationOpts_e.swThisConfiguration, null) is bool[] s && s.Length > 0 && s[0]) item["suppressed"] = true; } catch { }
                int err = f.GetErrorCode2(out bool warn);
                if (err != 0) { item["error"] = err; item["warning"] = warn; }
                features.Add(item);
            }
            var result = Json.O("document", doc.GetTitle(), "type", DocType(doc.GetType()), "featureCount", total, "features", features);
            if (doc is AssemblyDoc asm)
            {
                var comps = new List<object>();
                if (asm.GetComponents(true) is object[] all)
                    foreach (Component2 c in all.OfType<Component2>().Take(max))
                    {
                        var ci = Json.O("name", c.Name2, "path", c.GetPathName(), "configuration", c.ReferencedConfiguration);
                        if (c.IsSuppressed()) ci["suppressed"] = true;
                        if (c.IsFixed()) ci["fixed"] = true;
                        try { ci["constraint"] = ConstraintName(c.GetConstrainedStatus()); } catch { }
                        comps.Add(ci);
                    }
                result["components"] = comps;
                result["mateErrors"] = MateErrors(doc);
            }
            return result;
        }

        static string ConstraintName(int s) => s == 3 ? "fully" : s == 2 ? "under" : s == 4 ? "over" : "fixed/unknown";

        static List<object> MateErrors(ModelDoc2 doc)
        {
            var errors = new List<object>();
            for (var f = doc.FirstFeature() as Feature; f != null; f = f.GetNextFeature() as Feature)
            {
                if (f.GetTypeName2() != "MateGroup") continue;
                for (var m = f.GetFirstSubFeature() as Feature; m != null; m = m.GetNextSubFeature() as Feature)
                {
                    int e = m.GetErrorCode2(out bool warn);
                    if (e != 0) errors.Add(Json.O("mate", m.Name, "error", e, "warning", warn));
                }
            }
            return errors;
        }

        // ------------------------------------------------------------------ checks
        public Dictionary<string, object> Check(bool rebuild)
        {
            var doc = Doc();
            if (rebuild) doc.ForceRebuild3(false);
            var result = Json.O("document", doc.GetTitle(), "rebuilt", rebuild, "whatsWrongCount", doc.Extension.GetWhatsWrongCount());
            var errors = new List<object>();
            var underDefined = new List<string>();
            for (var f = doc.FirstFeature() as Feature; f != null; f = f.GetNextFeature() as Feature)
            {
                int e = f.GetErrorCode2(out bool warn);
                if (e != 0) errors.Add(Json.O("feature", f.Name, "error", e, "warning", warn));
                if (f.GetTypeName2() == "ProfileFeature" && f.GetSpecificFeature2() is Sketch sk)
                    try { if (sk.GetConstrainedStatus() == 2) underDefined.Add(f.Name); } catch { }
            }
            result["featureErrors"] = errors;
            result["underDefinedSketches"] = underDefined;
            if (doc is PartDoc part && part.GetBodies2((int)swBodyType_e.swSolidBody, true) is object[] bodies)
                result["solidBodies"] = bodies.Length;
            try
            {
                var mp = (MassProperty)doc.Extension.CreateMassProperty();
                if (mp != null) result["volume_mm3"] = R(mp.Volume * 1e9, 2);
            }
            catch { }
            if (doc is AssemblyDoc) result["mateErrors"] = MateErrors(doc);
            return result;
        }

        public Dictionary<string, object> MassProperties()
        {
            var doc = Doc();
            var mp = (MassProperty)doc.Extension.CreateMassProperty();
            if (mp == null) throw new InvalidOperationException("Mass properties are not available for this document.");
            var result = Json.O("document", doc.GetTitle(), "mass_g", R(mp.Mass * 1000, 3), "volume_mm3", R(mp.Volume * 1e9, 2),
                "surfaceArea_mm2", R(mp.SurfaceArea * 1e6, 2), "centerOfMass_mm", Mm(mp.CenterOfMass));
            if (doc is PartDoc part)
                try { result["material"] = part.GetMaterialPropertyName2("", out string db); } catch { }
            return result;
        }

        public Dictionary<string, object> CustomProperties()
        {
            var doc = Doc();
            var result = Json.O("document", doc.GetTitle());
            Dictionary<string, object> Read(string config)
            {
                var props = new Dictionary<string, object>();
                var mgr = doc.Extension.get_CustomPropertyManager(config);
                if (mgr?.GetNames() is object[] names)
                    foreach (string n in names.OfType<string>())
                    {
                        mgr.Get6(n, false, out string val, out string resolved, out bool wasResolved, out bool link);
                        props[n] = val == resolved ? (object)val : Json.O("value", val, "resolved", resolved);
                    }
                return props;
            }
            result["file"] = Read("");
            try
            {
                var cfg = doc.ConfigurationManager.ActiveConfiguration.Name;
                result["configuration"] = cfg;
                result["configurationProperties"] = Read(cfg);
            }
            catch { }
            return result;
        }

        public string RunMacro(string path, string module, string procedure)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) throw new FileNotFoundException("Macro not found", path);
            if (string.IsNullOrEmpty(module))
                module = Path.GetExtension(path).Equals(".swb", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(path) : "Module1";
            if (string.IsNullOrEmpty(procedure)) procedure = "main";
            bool ok = app().RunMacro2(path, module, procedure, (int)swRunMacroOption_e.swRunMacroUnloadAfterRun, out int err);
            return ok ? "Macro ran: " + Path.GetFileName(path) + " " + module + "." + procedure
                      : "Macro failed (swRunMacroError " + err + "): " + path;
        }

        // ------------------------------------------------------------------ screenshot
        static readonly Dictionary<string, int> NamedViews = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "front", 1 }, { "back", 2 }, { "left", 3 }, { "right", 4 }, { "top", 5 }, { "bottom", 6 },
            { "isometric", 7 }, { "trimetric", 8 }, { "dimetric", 9 },
        };

        /// <summary>PNG of the active viewport. A named view is shown temporarily and the user's view restored after.</summary>
        public byte[] Screenshot(string view, bool zoomToFit, int width)
        {
            var doc = Doc();
            var mv = doc.ActiveView as ModelView;
            MathTransform orientation = null;
            MathVector translation = null;
            double scale = 0;
            bool changeView = !string.IsNullOrEmpty(view) && !view.Equals("current", StringComparison.OrdinalIgnoreCase);
            if (changeView && mv != null)
            {
                orientation = mv.Orientation3; translation = mv.Translation3; scale = mv.Scale2;
                if (NamedViews.TryGetValue(view, out int id)) doc.ShowNamedView2("", id);
                else doc.ShowNamedView2(view.StartsWith("*") ? view : "*" + view, -1);
            }
            if (zoomToFit) doc.ViewZoomtofit2();
            doc.GraphicsRedraw2();

            var bmp = Path.Combine(Path.GetTempPath(), "cadmaster_view_" + Guid.NewGuid().ToString("N") + ".bmp");
            try
            {
                if (!doc.SaveBMP(bmp, 0, 0) || !File.Exists(bmp)) throw new InvalidOperationException("SOLIDWORKS could not save the view image.");
                using (var src = new Bitmap(bmp))
                {
                    int w = width > 0 && src.Width > width ? width : src.Width;
                    int h = (int)Math.Round(src.Height * (w / (double)src.Width));
                    using (var dst = new Bitmap(w, h))
                    using (var g = Graphics.FromImage(dst))
                    using (var ms = new MemoryStream())
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(src, 0, 0, w, h);
                        dst.Save(ms, ImageFormat.Png);
                        return ms.ToArray();
                    }
                }
            }
            finally
            {
                try { File.Delete(bmp); } catch { }
                if (changeView && mv != null && orientation != null)
                {
                    try { mv.Orientation3 = orientation; mv.Translation3 = translation; mv.Scale2 = scale; doc.GraphicsRedraw2(); } catch { }
                }
            }
        }
    }
}
