using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Game.Core
{
    public enum JsonKind { Null, Bool, Number, String, Array, Object }

    /// <summary>Minimal JSON tree. Numbers keep their source text so nothing is lost or reformatted.</summary>
    public sealed class JsonValue
    {
        private JsonValue(JsonKind kind) { Kind = kind; }
        public JsonKind Kind { get; }
        public bool Bool { get; private set; }
        /// <summary>String value, or the raw text of a number.</summary>
        public string Text { get; private set; }
        public List<JsonValue> Items { get; private set; }
        public List<KeyValuePair<string, JsonValue>> Members { get; private set; }

        public static JsonValue Null() => new JsonValue(JsonKind.Null);
        public static JsonValue Of(bool b) => new JsonValue(JsonKind.Bool) { Bool = b };
        public static JsonValue Of(string s) => new JsonValue(JsonKind.String) { Text = s };
        public static JsonValue Of(long n) => new JsonValue(JsonKind.Number) { Text = n.ToString(CultureInfo.InvariantCulture) };
        public static JsonValue NumberText(string raw) => new JsonValue(JsonKind.Number) { Text = raw };
        public static JsonValue NewArray() => new JsonValue(JsonKind.Array) { Items = new List<JsonValue>() };
        public static JsonValue NewObject() => new JsonValue(JsonKind.Object) { Members = new List<KeyValuePair<string, JsonValue>>() };

        public JsonValue Add(string key, JsonValue v) { Members.Add(new KeyValuePair<string, JsonValue>(key, v)); return this; }

        public JsonValue Get(string key)
        {
            if (Kind != JsonKind.Object) return null;
            foreach (var m in Members) if (m.Key == key) return m.Value;
            return null;
        }

        public bool TryGetInt64(out long v)
        {
            v = 0;
            return Kind == JsonKind.Number && long.TryParse(Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v);
        }

        public bool TryGetDouble(out double v)
        {
            v = 0;
            return Kind == JsonKind.Number && double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }
    }

    public sealed class JsonParseException : Exception
    {
        public JsonParseException(string message, int line, int column) : base(message + " at line " + line + ", column " + column)
        { Line = line; Column = column; }
        public int Line { get; }
        public int Column { get; }
    }

    /// <summary>Strict RFC 8259 reader and canonical writer. Culture-invariant; no reflection.</summary>
    public static class Json
    {
        private const int MaxDepth = 128;

        public static JsonValue Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var p = new Parser(text);
            p.SkipWs();
            var v = p.ParseValue(0);
            p.SkipWs();
            if (p.Pos != text.Length) throw p.Error("Unexpected content after the JSON value");
            return v;
        }

        public static string Write(JsonValue v) { var sb = new StringBuilder(); Write(v, sb); return sb.ToString(); }

        public static void Write(JsonValue v, StringBuilder sb)
        {
            switch (v.Kind)
            {
                case JsonKind.Null: sb.Append("null"); break;
                case JsonKind.Bool: sb.Append(v.Bool ? "true" : "false"); break;
                case JsonKind.Number: sb.Append(v.Text); break;
                case JsonKind.String: WriteString(v.Text, sb); break;
                case JsonKind.Array:
                    sb.Append('[');
                    for (int i = 0; i < v.Items.Count; i++) { if (i > 0) sb.Append(','); Write(v.Items[i], sb); }
                    sb.Append(']');
                    break;
                default:
                    sb.Append('{');
                    for (int i = 0; i < v.Members.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        WriteString(v.Members[i].Key, sb); sb.Append(':'); Write(v.Members[i].Value, sb);
                    }
                    sb.Append('}');
                    break;
            }
        }

        public static void WriteString(string s, StringBuilder sb)
        {
            sb.Append('"');
            foreach (char c in s)
            {
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

        private sealed class Parser
        {
            private readonly string _s;
            internal int Pos;
            internal Parser(string s) { _s = s; }

            internal JsonParseException Error(string message)
            {
                int line = 1, col = 1;
                for (int i = 0; i < Pos && i < _s.Length; i++) { if (_s[i] == '\n') { line++; col = 1; } else col++; }
                return new JsonParseException(message, line, col);
            }

            internal void SkipWs()
            {
                while (Pos < _s.Length && (_s[Pos] == ' ' || _s[Pos] == '\t' || _s[Pos] == '\n' || _s[Pos] == '\r')) Pos++;
            }

            internal JsonValue ParseValue(int depth)
            {
                if (depth > MaxDepth) throw Error("JSON nested too deeply");
                if (Pos >= _s.Length) throw Error("Unexpected end of input");
                char c = _s[Pos];
                switch (c)
                {
                    case '{': return ParseObject(depth);
                    case '[': return ParseArray(depth);
                    case '"': return JsonValue.Of(ParseString());
                    case 't': Expect("true"); return JsonValue.Of(true);
                    case 'f': Expect("false"); return JsonValue.Of(false);
                    case 'n': Expect("null"); return JsonValue.Null();
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                        throw Error("Unexpected character '" + c + "'");
                }
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(_s, Pos, word, 0, word.Length) != 0) throw Error("Invalid literal, expected " + word);
                Pos += word.Length;
            }

            private JsonValue ParseObject(int depth)
            {
                var obj = JsonValue.NewObject();
                Pos++; SkipWs();
                if (Pos < _s.Length && _s[Pos] == '}') { Pos++; return obj; }
                while (true)
                {
                    SkipWs();
                    if (Pos >= _s.Length || _s[Pos] != '"') throw Error("Expected a member name");
                    string key = ParseString();
                    SkipWs();
                    if (Pos >= _s.Length || _s[Pos] != ':') throw Error("Expected ':'");
                    Pos++; SkipWs();
                    if (obj.Get(key) != null) throw Error("Duplicate member '" + key + "'");
                    obj.Add(key, ParseValue(depth + 1));
                    SkipWs();
                    if (Pos >= _s.Length) throw Error("Unterminated object");
                    if (_s[Pos] == ',') { Pos++; continue; }
                    if (_s[Pos] == '}') { Pos++; return obj; }
                    throw Error("Expected ',' or '}'");
                }
            }

            private JsonValue ParseArray(int depth)
            {
                var arr = JsonValue.NewArray();
                Pos++; SkipWs();
                if (Pos < _s.Length && _s[Pos] == ']') { Pos++; return arr; }
                while (true)
                {
                    SkipWs();
                    arr.Items.Add(ParseValue(depth + 1));
                    SkipWs();
                    if (Pos >= _s.Length) throw Error("Unterminated array");
                    if (_s[Pos] == ',') { Pos++; continue; }
                    if (_s[Pos] == ']') { Pos++; return arr; }
                    throw Error("Expected ',' or ']'");
                }
            }

            private string ParseString()
            {
                Pos++; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (Pos >= _s.Length) throw Error("Unterminated string");
                    char c = _s[Pos++];
                    if (c == '"') return sb.ToString();
                    if (c < 0x20) { Pos--; throw Error("Control character in string"); }
                    if (c != '\\') { sb.Append(c); continue; }
                    if (Pos >= _s.Length) throw Error("Unterminated escape");
                    char e = _s[Pos++];
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
                        case 'u':
                            if (Pos + 4 > _s.Length || !int.TryParse(_s.Substring(Pos, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code))
                                throw Error("Invalid \\u escape");
                            sb.Append((char)code); Pos += 4; break;
                        default: Pos--; throw Error("Invalid escape '\\" + e + "'");
                    }
                }
            }

            private JsonValue ParseNumber()
            {
                int start = Pos;
                if (_s[Pos] == '-') Pos++;
                if (Pos < _s.Length && _s[Pos] == '0') Pos++;
                else if (!Digits()) throw Error("Invalid number");
                if (Pos < _s.Length && _s[Pos] == '.') { Pos++; if (!Digits()) throw Error("Invalid number fraction"); }
                if (Pos < _s.Length && (_s[Pos] == 'e' || _s[Pos] == 'E'))
                {
                    Pos++;
                    if (Pos < _s.Length && (_s[Pos] == '+' || _s[Pos] == '-')) Pos++;
                    if (!Digits()) throw Error("Invalid number exponent");
                }
                return JsonValue.NumberText(_s.Substring(start, Pos - start));
            }

            private bool Digits()
            {
                int start = Pos;
                while (Pos < _s.Length && _s[Pos] >= '0' && _s[Pos] <= '9') Pos++;
                return Pos > start;
            }
        }
    }
}
