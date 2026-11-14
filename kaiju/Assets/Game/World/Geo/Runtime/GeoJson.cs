// Minimal deterministic JSON reader/writer for geo_tile@v1 (engine-free; World.Geo may not use Core internals).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Game.World.Geo
{
    /// <summary>Parses JSON into Dictionary&lt;string, object&gt; (ordinal), List&lt;object&gt;, double, string, bool, null.</summary>
    public static class GeoJson
    {
        public static object Parse(string s)
        {
            int i = 0;
            var v = Value(s, ref i);
            Ws(s, ref i);
            if (i != s.Length) throw new FormatException("trailing data at " + i);
            return v;
        }

        static void Ws(string s, ref int i) { while (i < s.Length && (s[i] == ' ' || s[i] == '\n' || s[i] == '\r' || s[i] == '\t')) i++; }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end");
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>(StringComparer.Ordinal);
                i++; Ws(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i);
                    var k = Str(s, ref i);
                    Ws(s, ref i);
                    if (s[i] != ':') throw new FormatException("':' expected at " + i);
                    i++;
                    d[k] = Value(s, ref i);
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException("',' or '}' expected at " + i);
                }
            }
            if (c == '[')
            {
                var l = new List<object>();
                i++; Ws(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw new FormatException("',' or ']' expected at " + i);
                }
            }
            if (c == '"') return Str(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (st == i) throw new FormatException("unexpected '" + c + "' at " + i);
            return double.Parse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static string Str(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("string expected at " + i);
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: throw new FormatException("bad escape at " + i);
                }
            }
        }

        // ---- typed access helpers
        public static IReadOnlyDictionary<string, object> Obj(object o) => o as Dictionary<string, object>;
        public static List<object> Arr(object o) => o as List<object>;
        public static object Get(IReadOnlyDictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) ? v : null;
        public static string Str(IReadOnlyDictionary<string, object> d, string k) => Get(d, k) as string;
        public static double Num(IReadOnlyDictionary<string, object> d, string k, double fallback = double.NaN) => Get(d, k) is double x ? x : fallback;
        public static bool Bool(IReadOnlyDictionary<string, object> d, string k) => Get(d, k) is bool b ? b : (Get(d, k) is double x && x != 0);

        // ---- writer (sorted keys, invariant culture, round-trip doubles)
        public static string Write(object v)
        {
            var sb = new StringBuilder();
            W(sb, v);
            return sb.ToString();
        }

        static void W(StringBuilder sb, object v)
        {
            switch (v)
            {
                case null: sb.Append("null"); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case string s: Q(sb, s); return;
                case double d: Num(sb, d); return;
                case float f: Num(sb, f); return;
                case int n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); return;
                case long n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); return;
                case IDictionary<string, object> d:
                {
                    var keys = new List<string>(d.Keys);
                    keys.Sort(StringComparer.Ordinal);
                    sb.Append('{');
                    for (int i = 0; i < keys.Count; i++) { if (i > 0) sb.Append(','); Q(sb, keys[i]); sb.Append(':'); W(sb, d[keys[i]]); }
                    sb.Append('}');
                    return;
                }
                case System.Collections.IEnumerable e:
                {
                    sb.Append('[');
                    bool first = true;
                    foreach (var x in e) { if (!first) sb.Append(','); first = false; W(sb, x); }
                    sb.Append(']');
                    return;
                }
                default: Q(sb, v.ToString()); return;
            }
        }

        static void Num(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("null"); return; }
            sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        static void Q(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            sb.Append('"');
        }
    }
}
