using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Game.Core
{
    /// <summary>
    /// Canonical JSON form of a SaveDocument (envelope schema 1). Sections, records and fields are written in
    /// ordinal order, one record per line, "\n" line ends, invariant culture, floats as IEEE-754 bit patterns:
    /// the same document always gives the same bytes.
    /// </summary>
    public static class SaveCodec
    {
        public const string Format = "kaiju-save";

        public static string Serialize(SaveDocument doc)
        {
            var sb = new StringBuilder();
            sb.Append("{\"format\":\"").Append(Format).Append("\",\"schema\":").Append(doc.Schema.ToString(CultureInfo.InvariantCulture))
              .Append(",\"build\":");
            Json.WriteString(doc.BuildVersion, sb);
            sb.Append(",\"sections\":[\n");
            var sections = doc.Sections;
            for (int s = 0; s < sections.Count; s++)
            {
                var sec = sections[s];
                sb.Append("{\"name\":"); Json.WriteString(sec.Name, sb);
                sb.Append(",\"version\":").Append(sec.Version.ToString(CultureInfo.InvariantCulture)).Append(",\"records\":[\n");
                var records = sec.Records;
                for (int r = 0; r < records.Count; r++)
                {
                    var rec = records[r];
                    sb.Append("{\"key\":"); Json.WriteString(rec.Key, sb); sb.Append(",\"fields\":{");
                    var names = rec.FieldNames;
                    for (int f = 0; f < names.Count; f++)
                    {
                        if (f > 0) sb.Append(',');
                        Json.WriteString(names[f], sb); sb.Append(':');
                        WriteValue(rec.Get(names[f]), sb);
                    }
                    sb.Append(r + 1 < records.Count ? "}},\n" : "}}\n");
                }
                sb.Append(s + 1 < sections.Count ? "]},\n" : "]}\n");
            }
            sb.Append("]}\n");
            return sb.ToString();
        }

        private static void WriteValue(SaveValue v, StringBuilder sb)
        {
            switch (v.Kind)
            {
                case SaveValueKind.Int64: sb.Append(v.AsInt64.ToString(CultureInfo.InvariantCulture)); break;
                case SaveValueKind.Float64: sb.Append("{\"f64\":\"").Append(v.Float64Bits.ToString("X16", CultureInfo.InvariantCulture)).Append("\"}"); break;
                case SaveValueKind.Bool: sb.Append(v.AsBool ? "true" : "false"); break;
                case SaveValueKind.String: Json.WriteString(v.AsString, sb); break;
                default: sb.Append(v.RawText); break;
            }
        }

        public static SaveLoadResult Deserialize(string text, string sourceName)
        {
            JsonValue root;
            try { root = Json.Parse(text); }
            catch (JsonParseException ex) { return Fail(SaveLoadStatus.Corrupt, sourceName + ": " + ex.Message); }
            if (root.Kind != JsonKind.Object || root.Get("format")?.Kind != JsonKind.String || root.Get("format").Text != Format)
                return Fail(SaveLoadStatus.Corrupt, sourceName + ": not a " + Format + " file");
            var schema = root.Get("schema");
            if (schema == null || !schema.TryGetInt64(out long version) || version < 1)
                return Fail(SaveLoadStatus.UnknownSchema, sourceName + ": missing or unknown schema version '" + (schema == null ? "none" : Json.Write(schema)) + "'; refusing to load");
            if (version > SaveDocument.CurrentSchema)
                return Fail(SaveLoadStatus.NewerSchema, sourceName + ": save schema " + version + " is newer than this build supports (" + SaveDocument.CurrentSchema + "); the file was not modified");
            // version == 1 here. A future schema 2 adds its envelope migration at this point.
            try
            {
                var build = root.Get("build");
                if (build == null || build.Kind != JsonKind.String) throw new FormatException("missing build version");
                var doc = new SaveDocument(build.Text);
                var sections = Require(root, "sections", JsonKind.Array);
                foreach (var s in sections.Items)
                {
                    if (!Require(s, "version", JsonKind.Number).TryGetInt64(out long sv) || sv < 0 || sv > int.MaxValue) throw new FormatException("bad section version");
                    var sec = doc.AddSection(Require(s, "name", JsonKind.String).Text, (int)sv);
                    foreach (var r in Require(s, "records", JsonKind.Array).Items)
                    {
                        var rec = sec.Add(Require(r, "key", JsonKind.String).Text);
                        foreach (var f in Require(r, "fields", JsonKind.Object).Members) rec.Set(f.Key, ReadValue(f.Value));
                    }
                }
                return new SaveLoadResult(SaveLoadStatus.Ok, doc, null);
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException)
            {
                return Fail(SaveLoadStatus.Corrupt, sourceName + ": " + ex.Message);
            }
        }

        private static SaveValue ReadValue(JsonValue v)
        {
            if (v.Kind == JsonKind.Number && v.TryGetInt64(out long i)) return SaveValue.Of(i);
            if (v.Kind == JsonKind.Bool) return SaveValue.Of(v.Bool);
            if (v.Kind == JsonKind.String) return SaveValue.Of(v.Text);
            if (v.Kind == JsonKind.Object && v.Members.Count == 1 && v.Members[0].Key == "f64" && v.Members[0].Value.Kind == JsonKind.String
                && v.Members[0].Value.Text.Length == 16
                && long.TryParse(v.Members[0].Value.Text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out long bits))
                return SaveValue.Of(BitConverter.Int64BitsToDouble(bits));
            return SaveValue.RawJson(Json.Write(v)); // a value this build does not understand: keep it verbatim
        }

        private static JsonValue Require(JsonValue obj, string key, JsonKind kind)
        {
            var v = obj.Kind == JsonKind.Object ? obj.Get(key) : null;
            if (v == null || v.Kind != kind) throw new FormatException("expected " + kind + " '" + key + "'");
            return v;
        }

        private static SaveLoadResult Fail(SaveLoadStatus status, string message) => new SaveLoadResult(status, null, message);
    }

    /// <summary>
    /// One JSON save file. Save writes "&lt;path&gt;.tmp" completely, flushes it to disk, then atomically
    /// replaces the old file, so a failure at any point leaves the previous save loadable.
    /// </summary>
    public sealed class JsonFileSaveStore : ISaveStore
    {
        private readonly Func<string, Stream> _openWrite;

        /// <param name="openWrite">Test seam for simulating write failures; defaults to creating a FileStream.</param>
        public JsonFileSaveStore(string path, Func<string, Stream> openWrite = null)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            _openWrite = openWrite ?? (p => new FileStream(p, FileMode.Create, FileAccess.Write, FileShare.None));
        }

        public string Path { get; }
        public string TempPath => Path + ".tmp";
        public bool Exists => File.Exists(Path);

        public SaveLoadResult Load()
        {
            if (!File.Exists(Path)) return new SaveLoadResult(SaveLoadStatus.Missing, null, "No save at " + Path);
            return SaveCodec.Deserialize(File.ReadAllText(Path, Encoding.UTF8), Path);
        }

        public void Save(SaveDocument document)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(SaveCodec.Serialize(document));
            try
            {
                string parent = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                using (var stream = _openWrite(TempPath))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    if (stream is FileStream fs) fs.Flush(true); else stream.Flush();
                }
                if (File.Exists(Path)) File.Replace(TempPath, Path, null);
                else File.Move(TempPath, Path);
            }
            catch (Exception ex)
            {
                try { if (File.Exists(TempPath)) File.Delete(TempPath); } catch (IOException) { }
                throw new SaveWriteException("Saving " + Path + " failed; the previous save is unchanged.", ex);
            }
        }
    }

    /// <summary>
    /// Connects modules to the save: each ISaveParticipant owns one section. Sections nobody owns are carried
    /// over from the loaded document unchanged. Older sections are migrated step by step; newer ones are refused.
    /// </summary>
    public sealed class SaveSystem
    {
        private readonly SortedList<string, ISaveParticipant> _participants = new SortedList<string, ISaveParticipant>(StringComparer.Ordinal);
        private readonly List<ISaveSectionMigration> _migrations = new List<ISaveSectionMigration>();

        public SaveSystem(string buildVersion) { BuildVersion = buildVersion ?? throw new ArgumentNullException(nameof(buildVersion)); }
        public string BuildVersion { get; }

        public void Register(ISaveParticipant participant)
        {
            if (_participants.ContainsKey(participant.SectionName)) throw new ArgumentException("Save section " + participant.SectionName + " already has an owner");
            _participants.Add(participant.SectionName, participant);
        }

        public void Register(ISaveSectionMigration migration)
        {
            if (FindMigration(migration.SectionName, migration.FromVersion) != null)
                throw new ArgumentException("Duplicate migration " + migration.SectionName + " v" + migration.FromVersion);
            _migrations.Add(migration);
        }

        /// <summary>Builds a document from every participant; unowned sections of <paramref name="loaded"/> are kept.</summary>
        public SaveDocument Capture(SaveDocument loaded = null)
        {
            var doc = new SaveDocument(BuildVersion);
            if (loaded != null)
                foreach (var s in loaded.Sections)
                    if (!_participants.ContainsKey(s.Name)) CopyInto(doc, s);
            foreach (var p in _participants.Values) p.Write(doc.AddSection(p.SectionName, p.SectionVersion));
            return doc;
        }

        /// <summary>Validates every owned section first; applies nothing unless all of them can be read.</summary>
        public SaveLoadResult Restore(SaveDocument doc)
        {
            foreach (var p in _participants.Values)
            {
                if (!doc.TryGetSection(p.SectionName, out var s)) continue;
                if (s.Version > p.SectionVersion)
                    return new SaveLoadResult(SaveLoadStatus.SectionTooNew, null, "Section " + s.Name + " is v" + s.Version + " but this build reads up to v" + p.SectionVersion);
                for (int v = s.Version; v < p.SectionVersion; v++)
                    if (FindMigration(s.Name, v) == null)
                        return new SaveLoadResult(SaveLoadStatus.MissingMigration, null, "No migration for section " + s.Name + " from v" + v);
            }
            // Phase 2: snapshot current state, then migrate + read each owned section. A throwing Read (or migration) rolls the
            // already-applied participants back from the snapshot and returns Corrupt; nothing escapes.
            var backup = Capture();
            var applied = new List<ISaveParticipant>();
            try
            {
                foreach (var p in _participants.Values)
                {
                    if (!doc.TryGetSection(p.SectionName, out var s)) continue;
                    while (s.Version < p.SectionVersion)
                    {
                        int from = s.Version;
                        FindMigration(s.Name, from).Migrate(s);
                        if (s.Version != from + 1) s.Version = from + 1;
                    }
                    p.Read(s);
                    applied.Add(p);
                }
            }
            catch (Exception e)
            {
                foreach (var p in applied)
                {
                    if (!backup.TryGetSection(p.SectionName, out var b)) continue;
                    try { p.Read(b); } catch (Exception) { /* best effort: the participant's own Read is atomic per section */ }
                }
                return new SaveLoadResult(SaveLoadStatus.Corrupt, null, "Restore failed, earlier sections rolled back: " + e.Message);
            }
            return new SaveLoadResult(SaveLoadStatus.Ok, doc, null);
        }

        private ISaveSectionMigration FindMigration(string section, int from)
        {
            foreach (var m in _migrations) if (m.FromVersion == from && m.SectionName == section) return m;
            return null;
        }

        private static void CopyInto(SaveDocument doc, SaveSection source)
        {
            var copy = doc.AddSection(source.Name, source.Version);
            foreach (var r in source.Records)
            {
                var rc = copy.Add(r.Key);
                foreach (var f in r.FieldNames) rc.Set(f, r.Get(f));
            }
        }
    }
}
