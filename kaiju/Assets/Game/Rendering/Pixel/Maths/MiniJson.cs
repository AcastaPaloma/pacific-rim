using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Game.Rendering.Pixel
{
    /// <summary>
    /// A small strict JSON reader for the module's data files (palette_set@v1 has maps keyed by id, which Unity's
    /// JsonUtility cannot read). Objects become Dictionary&lt;string, object&gt; (insertion order is kept by a parallel key
    /// list in <see cref="Obj.Order"/>), arrays List&lt;object&gt;, numbers double, plus string, bool and null.
    /// </summary>
    public sealed class MiniJson
    {
        readonly string m_Text; int m_Pos;
        MiniJson(string text) { m_Text = text; }

        public static object Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var p = new MiniJson(text);
            object v = p.Value();
            p.Space();
            if (p.m_Pos != text.Length) throw p.Error("unexpected trailing text");
            return v;
        }

        /// <summary>Object with its keys in file order.</summary>
        public sealed class Obj : Dictionary<string, object>
        {
            public readonly List<string> Order = new List<string>();
            public Obj() : base(StringComparer.Ordinal) { }
            public string Str(string key) => (string)this[key];
            public double Num(string key) => (double)this[key];
            public Obj Object(string key) => (Obj)this[key];
            public List<object> Array(string key) => (List<object>)this[key];
            public bool Has(string key) => ContainsKey(key) && this[key] != null;
        }

        object Value()
        {
            Space();
            if (m_Pos >= m_Text.Length) throw Error("unexpected end");
            char c = m_Text[m_Pos];
            if (c == '{') return Object();
            if (c == '[') return Array();
            if (c == '"') return String();
            if (c == 't') { Expect("true"); return true; }
            if (c == 'f') { Expect("false"); return false; }
            if (c == 'n') { Expect("null"); return null; }
            return Number();
        }

        Obj Object()
        {
            var o = new Obj(); m_Pos++; Space();
            if (Peek() == '}') { m_Pos++; return o; }
            while (true)
            {
                Space(); string key = String(); Space();
                if (Peek() != ':') throw Error("expected ':'");
                m_Pos++;
                if (o.ContainsKey(key)) throw Error("duplicate key '" + key + "'");
                o[key] = Value(); o.Order.Add(key); Space();
                char c = Peek(); m_Pos++;
                if (c == '}') return o;
                if (c != ',') throw Error("expected ',' or '}'");
            }
        }

        List<object> Array()
        {
            var a = new List<object>(); m_Pos++; Space();
            if (Peek() == ']') { m_Pos++; return a; }
            while (true)
            {
                a.Add(Value()); Space();
                char c = Peek(); m_Pos++;
                if (c == ']') return a;
                if (c != ',') throw Error("expected ',' or ']'");
            }
        }

        string String()
        {
            if (Peek() != '"') throw Error("expected string");
            m_Pos++;
            var sb = new StringBuilder();
            while (true)
            {
                if (m_Pos >= m_Text.Length) throw Error("unterminated string");
                char c = m_Text[m_Pos++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = m_Text[m_Pos++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break; case 't': sb.Append('\t'); break; case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break; case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)int.Parse(m_Text.Substring(m_Pos, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)); m_Pos += 4; break;
                    default: sb.Append(e); break;
                }
            }
        }

        double Number()
        {
            int start = m_Pos;
            while (m_Pos < m_Text.Length && "+-0123456789.eE".IndexOf(m_Text[m_Pos]) >= 0) m_Pos++;
            if (start == m_Pos || !double.TryParse(m_Text.Substring(start, m_Pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                throw Error("bad number");
            return v;
        }

        void Expect(string word)
        {
            if (string.CompareOrdinal(m_Text, m_Pos, word, 0, word.Length) != 0) throw Error("expected " + word);
            m_Pos += word.Length;
        }

        char Peek() => m_Pos < m_Text.Length ? m_Text[m_Pos] : '\0';
        void Space() { while (m_Pos < m_Text.Length && char.IsWhiteSpace(m_Text[m_Pos])) m_Pos++; }
        FormatException Error(string message) => new FormatException("JSON: " + message + " at offset " + m_Pos);
    }
}
