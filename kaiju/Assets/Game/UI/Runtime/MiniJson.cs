using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Game.UI
{
    /// <summary>
    /// Tiny engine-free JSON reader/writer for UI data (settings, layout, colours). Objects are <see cref="SortedDictionary{TKey,TValue}"/>
    /// (ordinal key order), arrays <see cref="List{T}"/> of object, numbers double, so output is deterministic and round-trips byte-identically.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string s)
        {
            if (s == null) throw new FormatException("null json");
            int i = 0;
            object v = ReadValue(s, ref i);
            SkipWs(s, ref i);
            if (i != s.Length) throw new FormatException("trailing characters at " + i);
            return v;
        }

        public static bool TryParse(string s, out object value)
        {
            try { value = Parse(s); return true; }
            catch (FormatException) { value = null; return false; }
        }

        static void SkipWs(string s, ref int i) { while (i < s.Length && (s[i] == ' ' || s[i] == '\n' || s[i] == '\r' || s[i] == '\t')) i++; }

        static object ReadValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end");
            char c = s[i];
            if (c == '{')
            {
                i++;
                var d = new SortedDictionary<string, object>(StringComparer.Ordinal);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (true)
                {
                    SkipWs(s, ref i);
                    if (i >= s.Length || s[i] != '"') throw new FormatException("expected key at " + i);
                    string k = ReadString(s, ref i);
                    SkipWs(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw new FormatException("expected ':' at " + i);
                    i++;
                    d[k] = ReadValue(s, ref i);
                    SkipWs(s, ref i);
                    if (i >= s.Length) throw new FormatException("unterminated object");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException("expected ',' or '}' at " + i);
                }
            }
            if (c == '[')
            {
                i++;
                var l = new List<object>();
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(ReadValue(s, ref i));
                    SkipWs(s, ref i);
                    if (i >= s.Length) throw new FormatException("unterminated array");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw new FormatException("expected ',' or ']' at " + i);
                }
            }
            if (c == '"') return ReadString(s, ref i);
            if (Match(s, i, "true")) { i += 4; return true; }
            if (Match(s, i, "false")) { i += 5; return false; }
            if (Match(s, i, "null")) { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            double num;
            if (i == st || !double.TryParse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture, out num))
                throw new FormatException("bad token at " + st);
            return num;
        }

        static bool Match(string s, int i, string w) { return string.CompareOrdinal(s, i, w, 0, w.Length) == 0; }

        static string ReadString(string s, ref int i)
        {
            i++; // opening quote
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw new FormatException("unterminated string");
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) throw new FormatException("bad escape");
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        int code;
                        if (i + 4 > s.Length || !int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                            throw new FormatException("bad \\u escape");
                        sb.Append((char)code); i += 4; break;
                    default: throw new FormatException("bad escape \\" + e);
                }
            }
        }

        // ---- writer ----

        /// <summary>Indented (2 spaces) canonical text, newline at the end.</summary>
        public static string Write(object v)
        {
            var sb = new StringBuilder();
            WriteValue(sb, v, 0);
            sb.Append('\n');
            return sb.ToString();
        }

        static void Indent(StringBuilder sb, int n) { sb.Append(' ', n * 2); }

        static void WriteValue(StringBuilder sb, object v, int depth)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is string) { WriteString(sb, (string)v); return; }
            if (v is double || v is int || v is long || v is float) { WriteNumber(sb, Convert.ToDouble(v, CultureInfo.InvariantCulture)); return; }
            var dict = v as IDictionary<string, object>;
            if (dict != null)
            {
                if (dict.Count == 0) { sb.Append("{}"); return; }
                var keys = new List<string>(dict.Keys);
                keys.Sort(StringComparer.Ordinal);
                sb.Append("{\n");
                for (int i = 0; i < keys.Count; i++)
                {
                    Indent(sb, depth + 1); WriteString(sb, keys[i]); sb.Append(": ");
                    WriteValue(sb, dict[keys[i]], depth + 1);
                    sb.Append(i + 1 < keys.Count ? ",\n" : "\n");
                }
                Indent(sb, depth); sb.Append('}');
                return;
            }
            var list = v as IList<object>;
            if (list != null)
            {
                if (list.Count == 0) { sb.Append("[]"); return; }
                bool scalar = true;
                for (int i = 0; i < list.Count; i++) if (list[i] is IDictionary<string, object> || list[i] is IList<object>) { scalar = false; break; }
                if (scalar)
                {
                    sb.Append('[');
                    for (int i = 0; i < list.Count; i++) { if (i > 0) sb.Append(", "); WriteValue(sb, list[i], depth + 1); }
                    sb.Append(']');
                    return;
                }
                sb.Append("[\n");
                for (int i = 0; i < list.Count; i++)
                {
                    Indent(sb, depth + 1); WriteValue(sb, list[i], depth + 1);
                    sb.Append(i + 1 < list.Count ? ",\n" : "\n");
                }
                Indent(sb, depth); sb.Append(']');
                return;
            }
            throw new ArgumentException("unsupported json value type " + v.GetType().Name);
        }

        static void WriteNumber(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append('0'); return; }
            if (d == Math.Floor(d) && Math.Abs(d) < 1e15) sb.Append(((long)d).ToString(CultureInfo.InvariantCulture));
            else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---- typed accessors (lenient: missing or wrong type gives the default) ----

        public static IDictionary<string, object> Obj(object v) { return v as IDictionary<string, object>; }
        public static IList<object> Arr(object v) { return v as IList<object>; }

        public static object Get(IDictionary<string, object> o, string key)
        {
            object v;
            return o != null && o.TryGetValue(key, out v) ? v : null;
        }

        public static string Str(IDictionary<string, object> o, string key, string def)
        {
            var s = Get(o, key) as string; return s ?? def;
        }

        public static int Int(IDictionary<string, object> o, string key, int def)
        {
            var v = Get(o, key);
            if (!(v is double)) return def;
            double d = (double)v;
            if (double.IsNaN(d) || d < int.MinValue || d > int.MaxValue) return def;
            return (int)Math.Round(d);
        }

        public static bool Bool(IDictionary<string, object> o, string key, bool def)
        {
            var v = Get(o, key); return v is bool ? (bool)v : def;
        }
    }
}
