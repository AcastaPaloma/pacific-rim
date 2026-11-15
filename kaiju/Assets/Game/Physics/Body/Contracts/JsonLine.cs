using System.Globalization;
using System.Numerics;
using System.Text;

namespace Game.Physics.Body.Contracts
{
    /// <summary>Allocation-light writer for one JSON Lines record (gait_log@v1, physics_log@v1). Invariant culture, round-trip doubles.</summary>
    public sealed class JsonLine
    {
        readonly StringBuilder _sb = new StringBuilder(2048);
        bool _first = true;

        public JsonLine Begin() { _sb.Clear(); _sb.Append('{'); _first = true; return this; }
        public string End() { _sb.Append('}'); return _sb.ToString(); }
        void Key(string k) { if (!_first) _sb.Append(','); _first = false; _sb.Append('"').Append(k).Append("\":"); }
        public static void Num(StringBuilder sb, double v)
        {
            sb.Append(Text(v));
        }
        /// <summary>Shortest text that reads back to the same value: values that are exactly a float (everything measured from the engine) print as floats, real doubles as doubles.</summary>
        public static string Text(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "null";
            float f = (float)v;
            return (double)f == v ? f.ToString("R", CultureInfo.InvariantCulture) : v.ToString("R", CultureInfo.InvariantCulture);
        }
        public JsonLine Str(string k, string v) { Key(k); Quote(v); return this; }
        public JsonLine Num(string k, double v) { Key(k); Num(_sb, v); return this; }
        public JsonLine Int(string k, long v) { Key(k); _sb.Append(v.ToString(CultureInfo.InvariantCulture)); return this; }
        public JsonLine Bool(string k, bool v) { Key(k); _sb.Append(v ? "true" : "false"); return this; }
        public JsonLine Vec(string k, Vector3 v) { Key(k); AppendVec(v); return this; }
        public JsonLine Vec(string k, double x, double y, double z) { Key(k); _sb.Append('['); Num(_sb, x); _sb.Append(','); Num(_sb, y); _sb.Append(','); Num(_sb, z); _sb.Append(']'); return this; }
        public JsonLine Quat(string k, Quaternion q) { Key(k); _sb.Append('['); Num(_sb, q.X); _sb.Append(','); Num(_sb, q.Y); _sb.Append(','); Num(_sb, q.Z); _sb.Append(','); Num(_sb, q.W); _sb.Append(']'); return this; }
        public JsonLine Null(string k) { Key(k); _sb.Append("null"); return this; }
        /// <summary>Raw JSON value (already valid JSON).</summary>
        public JsonLine Raw(string k, string json) { Key(k); _sb.Append(json); return this; }
        public JsonLine Arr(string k, double[] v) { Key(k); _sb.Append(ArrayOf(v)); return this; }

        void AppendVec(Vector3 v) { _sb.Append('['); Num(_sb, v.X); _sb.Append(','); Num(_sb, v.Y); _sb.Append(','); Num(_sb, v.Z); _sb.Append(']'); }
        void Quote(string s)
        {
            _sb.Append('"');
            foreach (char c in s ?? "")
            {
                if (c == '"' || c == '\\') _sb.Append('\\').Append(c);
                else if (c < ' ') _sb.Append(' ');
                else _sb.Append(c);
            }
            _sb.Append('"');
        }
        public static string ArrayOf(double[] v)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < v.Length; i++) { if (i > 0) sb.Append(','); Num(sb, v[i]); }
            return sb.Append(']').ToString();
        }
        public static string VecOf(Vector3 v) => ArrayOf(new double[] { v.X, v.Y, v.Z });
        public static string StrOf(string s) => new JsonLine().Begin().Str("s", s).End().Substring(5).TrimEnd('}');
    }
}
