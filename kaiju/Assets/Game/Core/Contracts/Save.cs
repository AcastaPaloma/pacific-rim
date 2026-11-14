using System;
using System.Collections.Generic;

namespace Game.Core
{
    // Save contract. Envelope schema v1 (SaveDocument.CurrentSchema). A document holds named sections;
    // each section belongs to one module, carries that module's own version and holds ID-keyed records.
    // Core knows no section content. Sections nobody claims are kept and written back unchanged.

    public enum SaveValueKind : byte { Int64 = 1, Float64 = 2, Bool = 3, String = 4, Raw = 5 }

    /// <summary>One field value. Float64 is stored bit-exact. Raw = canonical JSON text of a value this build does not understand.</summary>
    public readonly struct SaveValue : IEquatable<SaveValue>
    {
        public readonly SaveValueKind Kind;
        private readonly long _bits;
        private readonly string _text;
        private SaveValue(SaveValueKind kind, long bits, string text) { Kind = kind; _bits = bits; _text = text; }

        public static SaveValue Of(long v) => new SaveValue(SaveValueKind.Int64, v, null);
        public static SaveValue Of(double v) => new SaveValue(SaveValueKind.Float64, BitConverter.DoubleToInt64Bits(v), null);
        public static SaveValue Of(bool v) => new SaveValue(SaveValueKind.Bool, v ? 1 : 0, null);
        public static SaveValue Of(string v) => new SaveValue(SaveValueKind.String, 0, v ?? throw new ArgumentNullException(nameof(v)));
        public static SaveValue RawJson(string canonicalJson) => new SaveValue(SaveValueKind.Raw, 0, canonicalJson);

        public long AsInt64 => Kind == SaveValueKind.Int64 ? _bits : throw Wrong(SaveValueKind.Int64);
        public double AsFloat64 => Kind == SaveValueKind.Float64 ? BitConverter.Int64BitsToDouble(_bits) : throw Wrong(SaveValueKind.Float64);
        public long Float64Bits => Kind == SaveValueKind.Float64 ? _bits : throw Wrong(SaveValueKind.Float64);
        public bool AsBool => Kind == SaveValueKind.Bool ? _bits != 0 : throw Wrong(SaveValueKind.Bool);
        public string AsString => Kind == SaveValueKind.String ? _text : throw Wrong(SaveValueKind.String);
        public string RawText => Kind == SaveValueKind.Raw ? _text : throw Wrong(SaveValueKind.Raw);

        private InvalidOperationException Wrong(SaveValueKind wanted) => new InvalidOperationException("Save value is " + Kind + ", not " + wanted);
        public bool Equals(SaveValue o) => Kind == o.Kind && _bits == o._bits && string.Equals(_text, o._text, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is SaveValue o && Equals(o);
        public override int GetHashCode() => (int)Kind ^ _bits.GetHashCode();
    }

    /// <summary>One ID-keyed record. Key is an ID's canonical string form (its ToString()).</summary>
    public sealed class SaveRecord
    {
        private readonly SortedList<string, SaveValue> _fields = new SortedList<string, SaveValue>(StringComparer.Ordinal);
        public SaveRecord(string key) { Key = string.IsNullOrEmpty(key) ? throw new ArgumentException("Empty record key") : key; }
        public string Key { get; }
        public IList<string> FieldNames => _fields.Keys;
        public SaveRecord Set(string field, SaveValue value) { _fields[field] = value; return this; }
        public bool TryGet(string field, out SaveValue value) => _fields.TryGetValue(field, out value);
        public SaveValue Get(string field) => _fields.TryGetValue(field, out var v) ? v : throw new KeyNotFoundException("Record " + Key + " has no field " + field);
        public bool Remove(string field) => _fields.Remove(field);
    }

    /// <summary>A module's part of the save. Records are kept in ordinal key order.</summary>
    public sealed class SaveSection
    {
        private readonly SortedList<string, SaveRecord> _records = new SortedList<string, SaveRecord>(StringComparer.Ordinal);
        public SaveSection(string name, int version)
        {
            Name = string.IsNullOrEmpty(name) ? throw new ArgumentException("Empty section name") : name;
            Version = version;
        }
        public string Name { get; }
        /// <summary>The owning module's section version. Migrations raise it one step at a time.</summary>
        public int Version { get; set; }
        public IList<SaveRecord> Records => _records.Values;
        public SaveRecord Add(string key)
        {
            if (_records.ContainsKey(key)) throw new ArgumentException("Duplicate record key " + key + " in section " + Name);
            var r = new SaveRecord(key); _records.Add(key, r); return r;
        }
        public bool TryGet(string key, out SaveRecord record) => _records.TryGetValue(key, out record);
        public bool Remove(string key) => _records.Remove(key);
    }

    public sealed class SaveDocument
    {
        public const int CurrentSchema = 1;
        private readonly SortedList<string, SaveSection> _sections = new SortedList<string, SaveSection>(StringComparer.Ordinal);
        public SaveDocument(string buildVersion) { BuildVersion = buildVersion ?? throw new ArgumentNullException(nameof(buildVersion)); }
        public int Schema => CurrentSchema;
        /// <summary>Version of the build that wrote (or will write) this document.</summary>
        public string BuildVersion { get; set; }
        public IList<SaveSection> Sections => _sections.Values;
        public SaveSection AddSection(string name, int version)
        {
            if (_sections.ContainsKey(name)) throw new ArgumentException("Duplicate section " + name);
            var s = new SaveSection(name, version); _sections.Add(name, s); return s;
        }
        public bool TryGetSection(string name, out SaveSection section) => _sections.TryGetValue(name, out section);
        public bool RemoveSection(string name) => _sections.Remove(name);
    }

    public enum SaveLoadStatus { Ok = 0, Missing = 1, Corrupt = 2, NewerSchema = 3, UnknownSchema = 4, SectionTooNew = 5, MissingMigration = 6 }

    public sealed class SaveLoadResult
    {
        public SaveLoadResult(SaveLoadStatus status, SaveDocument document, string message) { Status = status; Document = document; Message = message; }
        public SaveLoadStatus Status { get; }
        public SaveDocument Document { get; }   // null unless Status == Ok
        public string Message { get; }
        public bool Ok => Status == SaveLoadStatus.Ok;
    }

    /// <summary>Persists one SaveDocument. Writes are atomic: a failed write leaves the previous save intact.</summary>
    public interface ISaveStore
    {
        bool Exists { get; }
        SaveLoadResult Load();
        void Save(SaveDocument document);
    }

    /// <summary>Implemented by a module that owns a save section.</summary>
    public interface ISaveParticipant
    {
        string SectionName { get; }
        int SectionVersion { get; }
        void Write(SaveSection section);  // section is empty, Version == SectionVersion
        void Read(SaveSection section);   // section already migrated to SectionVersion
    }

    /// <summary>Upgrades one section from FromVersion to FromVersion + 1, in place.</summary>
    public interface ISaveSectionMigration
    {
        string SectionName { get; }
        int FromVersion { get; }
        void Migrate(SaveSection section);
    }

    public sealed class SaveWriteException : Exception
    {
        public SaveWriteException(string message, Exception inner) : base(message, inner) { }
    }
}
