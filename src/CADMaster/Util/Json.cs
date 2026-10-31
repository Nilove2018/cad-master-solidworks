using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace CADMaster
{
    /// <summary>JSON helpers on the built-in JavaScriptSerializer (no Newtonsoft: SOLIDWORKS ships its own copy).</summary>
    public static class Json
    {
        static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 512 };

        public static string Serialize(object value) => Serializer.Serialize(value);

        public static object Parse(string text) => Serializer.DeserializeObject(text);

        public static Dictionary<string, object> ParseObject(string text)
        {
            try { return Serializer.DeserializeObject(text) as Dictionary<string, object>; }
            catch { return null; }
        }

        public static T Deserialize<T>(string text) => Serializer.Deserialize<T>(text);

        public static string Str(this Dictionary<string, object> d, string key, string fallback = null)
        {
            if (d == null || !d.TryGetValue(key, out var v) || v == null) return fallback;
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static int Int(this Dictionary<string, object> d, string key, int fallback = 0)
        {
            if (d == null || !d.TryGetValue(key, out var v) || v == null) return fallback;
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); } catch { return fallback; }
        }

        public static double Dbl(this Dictionary<string, object> d, string key, double fallback = 0)
        {
            if (d == null || !d.TryGetValue(key, out var v) || v == null) return fallback;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch { return fallback; }
        }

        public static bool Bool(this Dictionary<string, object> d, string key, bool fallback = false)
        {
            if (d == null || !d.TryGetValue(key, out var v) || v == null) return fallback;
            return v is bool b ? b : fallback;
        }

        public static Dictionary<string, object> Obj(this Dictionary<string, object> d, string key)
        {
            if (d == null || !d.TryGetValue(key, out var v)) return null;
            return v as Dictionary<string, object>;
        }

        public static object[] Arr(this Dictionary<string, object> d, string key)
        {
            if (d == null || !d.TryGetValue(key, out var v)) return null;
            return v as object[];
        }

        /// <summary>Builds a dictionary from alternating key/value arguments.</summary>
        public static Dictionary<string, object> O(params object[] kv)
        {
            var d = new Dictionary<string, object>();
            for (int i = 0; i + 1 < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1];
            return d;
        }
    }
}
