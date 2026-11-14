using System;
using System.Globalization;

namespace Game.Core
{
    // ID contract v1. All IDs are value types with explicit construction only (no implicit conversions),
    // `default` = invalid, value-based equality/ordering/hashing, and a canonical invariant string form
    // ("ent:42", "bld:osm-way:123", "slot:0") that is also the save-record key.

    internal static class IdHash
    {
        // Deterministic 64->32 bit mix (SplitMix64 finaliser). Never uses string or object hashing.
        internal static int Mix(ulong x)
        {
            unchecked
            {
                x ^= x >> 30; x *= 0xBF58476D1CE4E5B9UL;
                x ^= x >> 27; x *= 0x94D049BB133111EBUL;
                x ^= x >> 31;
                return (int)x ^ (int)(x >> 32);
            }
        }

        internal static bool TryParseUInt64(string s, int start, out ulong value)
        {
            value = 0;
            if (s == null || start >= s.Length) return false;
            for (int i = start; i < s.Length; i++) if (s[i] < '0' || s[i] > '9') return false;
            return ulong.TryParse(s.Substring(start), NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }
    }

    /// <summary>Runtime entity (mech, kaiju, part, debris). Issued by EntityIdAllocator, never reused in a session.</summary>
    public readonly struct EntityId : IEquatable<EntityId>, IComparable<EntityId>
    {
        public readonly ulong Value;
        private EntityId(ulong value) { Value = value; }
        public static EntityId FromValue(ulong value) => new EntityId(value);
        public bool IsValid => Value != 0;
        public bool Equals(EntityId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is EntityId o && Equals(o);
        public override int GetHashCode() => IdHash.Mix(Value);
        public int CompareTo(EntityId other) => Value.CompareTo(other.Value);
        public static bool operator ==(EntityId a, EntityId b) => a.Value == b.Value;
        public static bool operator !=(EntityId a, EntityId b) => a.Value != b.Value;
        public override string ToString() => "ent:" + Value.ToString(CultureInfo.InvariantCulture);
        public static bool TryParse(string s, out EntityId id)
        {
            id = default;
            if (s == null || !s.StartsWith("ent:", StringComparison.Ordinal) || !IdHash.TryParseUInt64(s, 4, out var v)) return false;
            id = new EntityId(v); return true;
        }
    }

    /// <summary>Namespace of a building's source key. Append only; never renumber (values are saved).</summary>
    public enum BuildingSource : byte
    {
        None = 0,
        OsmWay = 1,
        OsmRelation = 2,
        Landmark = 3,
    }

    /// <summary>
    /// Persistent building identity (F-043) = (source namespace, 64-bit source id), e.g. an OSM way id.
    /// Independent of load order and of which other buildings exist.
    /// </summary>
    public readonly struct BuildingId : IEquatable<BuildingId>, IComparable<BuildingId>
    {
        public readonly BuildingSource Source;
        public readonly ulong SourceId;
        private BuildingId(BuildingSource source, ulong sourceId) { Source = source; SourceId = sourceId; }

        public static BuildingId From(BuildingSource source, ulong sourceId)
        {
            if (TokenOf(source) == null || source == BuildingSource.None)
                throw new ArgumentOutOfRangeException(nameof(source), "Unknown building source " + (int)source);
            return new BuildingId(source, sourceId);
        }

        public bool IsValid => Source != BuildingSource.None;
        public bool Equals(BuildingId other) => Source == other.Source && SourceId == other.SourceId;
        public override bool Equals(object obj) => obj is BuildingId o && Equals(o);
        public override int GetHashCode() => IdHash.Mix(SourceId ^ ((ulong)Source * 0x9E3779B97F4A7C15UL));
        public int CompareTo(BuildingId other) => Source != other.Source ? Source.CompareTo(other.Source) : SourceId.CompareTo(other.SourceId);
        public static bool operator ==(BuildingId a, BuildingId b) => a.Equals(b);
        public static bool operator !=(BuildingId a, BuildingId b) => !a.Equals(b);

        public override string ToString() => "bld:" + TokenOf(Source) + ":" + SourceId.ToString(CultureInfo.InvariantCulture);

        public static bool TryParse(string s, out BuildingId id)
        {
            id = default;
            if (s == null || !s.StartsWith("bld:", StringComparison.Ordinal)) return false;
            int colon = s.IndexOf(':', 4);
            if (colon < 0) return false;
            string token = s.Substring(4, colon - 4);
            for (int i = 1; i <= 3; i++)
            {
                var src = (BuildingSource)i;
                if (TokenOf(src) == token && IdHash.TryParseUInt64(s, colon + 1, out var v)) { id = new BuildingId(src, v); return true; }
            }
            return false;
        }

        private static string TokenOf(BuildingSource s)
        {
            switch (s)
            {
                case BuildingSource.None: return "none";
                case BuildingSource.OsmWay: return "osm-way";
                case BuildingSource.OsmRelation: return "osm-relation";
                case BuildingSource.Landmark: return "landmark";
                default: return null;
            }
        }
    }

    /// <summary>A player slot (D-004). Zero-based index; there is no maximum and no count anywhere in Core.</summary>
    public readonly struct PlayerSlot : IEquatable<PlayerSlot>, IComparable<PlayerSlot>
    {
        private readonly int _indexPlusOne;
        private PlayerSlot(int indexPlusOne) { _indexPlusOne = indexPlusOne; }

        public static PlayerSlot FromIndex(int index)
        {
            if (index < 0 || index == int.MaxValue) throw new ArgumentOutOfRangeException(nameof(index));
            return new PlayerSlot(index + 1);
        }

        /// <summary>Slots 0..count-1 for a session with <paramref name="count"/> players.</summary>
        public static PlayerSlot[] Range(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            var slots = new PlayerSlot[count];
            for (int i = 0; i < count; i++) slots[i] = new PlayerSlot(i + 1);
            return slots;
        }

        public bool IsValid => _indexPlusOne != 0;
        public int Index => _indexPlusOne != 0 ? _indexPlusOne - 1 : throw new InvalidOperationException("Invalid PlayerSlot");
        public bool Equals(PlayerSlot other) => _indexPlusOne == other._indexPlusOne;
        public override bool Equals(object obj) => obj is PlayerSlot o && Equals(o);
        public override int GetHashCode() => IdHash.Mix((ulong)_indexPlusOne);
        public int CompareTo(PlayerSlot other) => _indexPlusOne.CompareTo(other._indexPlusOne);
        public static bool operator ==(PlayerSlot a, PlayerSlot b) => a._indexPlusOne == b._indexPlusOne;
        public static bool operator !=(PlayerSlot a, PlayerSlot b) => a._indexPlusOne != b._indexPlusOne;
        public override string ToString() => IsValid ? "slot:" + Index.ToString(CultureInfo.InvariantCulture) : "slot:none";

        public static bool TryParse(string s, out PlayerSlot slot)
        {
            slot = default;
            if (s == null || !s.StartsWith("slot:", StringComparison.Ordinal) || !IdHash.TryParseUInt64(s, 5, out var v) || v >= int.MaxValue) return false;
            slot = new PlayerSlot((int)v + 1); return true;
        }
    }
}
