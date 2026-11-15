using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Game.Net
{
    public enum NetLogKind : byte
    {
        /// <summary>A = slot index assigned. Text = warnings joined by " | ".</summary>
        HandshakeAccept = 0,
        /// <summary>A = RefuseCode bits. Text = details joined by " | ".</summary>
        HandshakeRefuse = 1,
        /// <summary>A = intents dropped as stale or duplicate since the previous record.</summary>
        IntentDropStale = 2,
        /// <summary>A = intents dropped for unknown slot, spoofed slot or malformed datagram since the previous record.</summary>
        IntentDropOther = 3,
        /// <summary>Slot's last intent was repeated for this host step (hold window).</summary>
        IntentHold = 4,
        /// <summary>Slot received the neutral intent for this host step.</summary>
        IntentNeutral = 5,
        /// <summary>A = 1 when the interpolator turned stale, 0 when it recovered.</summary>
        SnapshotStale = 6,
        /// <summary>One complete second at the ITransport boundary: A = bytes down (host to client), B = bytes up, C = packets down, D = packets up.</summary>
        Bandwidth = 7,
    }

    public readonly struct NetLogRecord
    {
        public readonly long Step;
        public readonly NetLogKind Kind;
        /// <summary>Slot index, or -1 when not slot specific.</summary>
        public readonly int Slot;
        public readonly long A, B, C, D;
        public readonly string Text;

        public NetLogRecord(long step, NetLogKind kind, int slot, long a = 0, long b = 0, long c = 0, long d = 0, string text = "")
        {
            Step = step; Kind = kind; Slot = slot; A = a; B = b; C = c; D = d; Text = text ?? "";
        }
    }

    /// <summary>
    /// In-memory net_log@v1: typed records keyed by Core step, appended by the session wrappers, with a deterministic
    /// text form (tab separated) and a JSON Lines form. Both start with the header "net_log@v1". The step is whatever
    /// step the caller passes to the session (a Core step in the game). Invariant culture, no wall clock.
    /// </summary>
    public sealed class NetLog
    {
        public const string Header = "net_log@v1";

        private readonly List<NetLogRecord> _records = new List<NetLogRecord>();
        public IReadOnlyList<NetLogRecord> Records => _records;

        public void Add(in NetLogRecord r) => _records.Add(r);
        public void Add(long step, NetLogKind kind, int slot = -1, long a = 0, long b = 0, long c = 0, long d = 0, string text = "") =>
            _records.Add(new NetLogRecord(step, kind, slot, a, b, c, d, text));

        public int Count(NetLogKind kind)
        {
            int n = 0;
            foreach (var r in _records) if (r.Kind == kind) n++;
            return n;
        }

        public static string KindName(NetLogKind k)
        {
            switch (k)
            {
                case NetLogKind.HandshakeAccept: return "handshake_accept";
                case NetLogKind.HandshakeRefuse: return "handshake_refuse";
                case NetLogKind.IntentDropStale: return "intent_drop_stale";
                case NetLogKind.IntentDropOther: return "intent_drop_other";
                case NetLogKind.IntentHold: return "intent_hold";
                case NetLogKind.IntentNeutral: return "intent_neutral";
                case NetLogKind.SnapshotStale: return "snapshot_stale";
                case NetLogKind.Bandwidth: return "bandwidth";
                default: return "unknown";
            }
        }

        /// <summary>Header line, then one line per record: step, kind, slot, a, b, c, d, text (tab separated, text escaped), LF endings.</summary>
        public string ToText()
        {
            var sb = new StringBuilder();
            sb.Append(Header).Append('\n');
            foreach (var r in _records)
            {
                sb.Append(L(r.Step)).Append('\t').Append(KindName(r.Kind)).Append('\t').Append(L(r.Slot)).Append('\t')
                  .Append(L(r.A)).Append('\t').Append(L(r.B)).Append('\t').Append(L(r.C)).Append('\t').Append(L(r.D)).Append('\t');
                foreach (char ch in r.Text)
                {
                    if (ch == '\\') sb.Append("\\\\");
                    else if (ch == '\t') sb.Append("\\t");
                    else if (ch == '\n') sb.Append("\\n");
                    else if (ch == '\r') sb.Append("\\r");
                    else sb.Append(ch);
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>First line {"schema":"net_log@v1"}, then one JSON object per record.</summary>
        public string ToJsonLines()
        {
            var sb = new StringBuilder();
            sb.Append("{\"schema\":\"").Append(Header).Append("\"}\n");
            foreach (var r in _records)
            {
                sb.Append("{\"step\":").Append(L(r.Step)).Append(",\"kind\":\"").Append(KindName(r.Kind)).Append("\",\"slot\":").Append(L(r.Slot))
                  .Append(",\"a\":").Append(L(r.A)).Append(",\"b\":").Append(L(r.B)).Append(",\"c\":").Append(L(r.C)).Append(",\"d\":").Append(L(r.D))
                  .Append(",\"text\":\"");
                foreach (char ch in r.Text)
                {
                    if (ch == '"') sb.Append("\\\"");
                    else if (ch == '\\') sb.Append("\\\\");
                    else if (ch == '\n') sb.Append("\\n");
                    else if (ch == '\r') sb.Append("\\r");
                    else if (ch == '\t') sb.Append("\\t");
                    else if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(ch);
                }
                sb.Append("\"}\n");
            }
            return sb.ToString();
        }

        private static string L(long v) => v.ToString(CultureInfo.InvariantCulture);
    }
}
