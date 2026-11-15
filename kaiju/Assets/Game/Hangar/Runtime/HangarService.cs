// hangar@v1 save section + the stub service (T-034). Engine-free.
using System;
using System.Collections.Generic;
using Game.Core;
using Game.World.Geo;

namespace Game.Hangar
{
    /// <summary>
    /// Section "hangar", version 1. Per body (key = PlayerSlot.ToString(), e.g. "slot:0"):
    ///   header record "slot:N": phase (Int64), cycle (Int64), site (String)
    ///   part records "slot:N/<partId>": integrity, max_integrity (Float64 bits), last_hit_step, hits (Int64), repaired (Bool), queue (Int64, -1 = none)
    /// Read validates everything first (finite, 0 &lt;= integrity &lt;= max, queue indices dense, known site) and throws FormatException
    /// leaving the service untouched, so SaveSystem.Restore can report Corrupt.
    /// </summary>
    public sealed class HangarService : IHangarService, ISaveParticipant
    {
        public const string FieldPhase = "phase", FieldCycle = "cycle", FieldSite = "site";
        public const string FieldIntegrity = "integrity", FieldMax = "max_integrity", FieldLastHit = "last_hit_step",
                            FieldHits = "hits", FieldRepaired = "repaired", FieldQueue = "queue";

        sealed class Body
        {
            public HangarPhase Phase;
            public long Cycle;
            public string SiteId = "";
            public List<PartRepairState> Parts = new List<PartRepairState>();   // ordinal part order
            public Body Clone() { var b = (Body)MemberwiseClone(); b.Parts = new List<PartRepairState>(Parts); return b; }
        }

        readonly SortedList<int, Body> _bodies = new SortedList<int, Body>();
        readonly IGeoWorld _world;

        public HangarService(HangarSite site, IGeoWorld world = null)
        {
            Site = site ?? throw new ArgumentNullException(nameof(site));
            _world = world;
        }

        public HangarSite Site { get; }

        // ---- ISaveParticipant
        public string SectionName => HangarContractVersions.SectionName;
        public int SectionVersion => HangarContractVersions.SaveSection;

        public void Write(SaveSection section)
        {
            foreach (var kv in _bodies)
            {
                string key = PlayerSlot.FromIndex(kv.Key).ToString();
                var b = kv.Value;
                section.Add(key).Set(FieldPhase, SaveValue.Of((long)b.Phase)).Set(FieldCycle, SaveValue.Of(b.Cycle)).Set(FieldSite, SaveValue.Of(b.SiteId));
                foreach (var p in b.Parts)
                    section.Add(key + "/" + p.Damage.PartId)
                        .Set(FieldIntegrity, SaveValue.Of(p.Damage.Integrity)).Set(FieldMax, SaveValue.Of(p.Damage.MaxIntegrity))
                        .Set(FieldLastHit, SaveValue.Of(p.Damage.LastHitStep)).Set(FieldHits, SaveValue.Of(p.Damage.Hits))
                        .Set(FieldRepaired, SaveValue.Of(p.Repaired)).Set(FieldQueue, SaveValue.Of(p.QueueIndex));
            }
        }

        public void Read(SaveSection section)
        {
            var bodies = new SortedList<int, Body>();
            // pass 1: headers
            foreach (var rec in section.Records)
            {
                if (rec.Key.IndexOf('/') >= 0) continue;
                if (!PlayerSlot.TryParse(rec.Key, out var slot) || !slot.IsValid || slot.ToString() != rec.Key)
                    throw new FormatException("hangar: bad body key '" + rec.Key + "'");
                long phase = Int(rec, FieldPhase);
                if (phase < 0 || phase > (long)HangarPhase.Hangar) throw new FormatException("hangar: phase " + phase + " out of range for " + rec.Key);
                long cycle = Int(rec, FieldCycle);
                if (cycle < 0) throw new FormatException("hangar: negative cycle for " + rec.Key);
                string site = Str(rec, FieldSite);
                if (!HangarSites.TryGet(site, out _)) throw new FormatException("hangar: unknown site '" + site + "' for " + rec.Key);
                bodies.Add(slot.Index, new Body { Phase = (HangarPhase)phase, Cycle = cycle, SiteId = site });
            }
            // pass 2: parts (records arrive in ordinal key order, so parts end up in ordinal part order)
            foreach (var rec in section.Records)
            {
                int cut = rec.Key.IndexOf('/');
                if (cut < 0) continue;
                string bodyKey = rec.Key.Substring(0, cut), partId = rec.Key.Substring(cut + 1);
                if (!PlayerSlot.TryParse(bodyKey, out var slot) || !bodies.TryGetValue(slot.Index, out var body))
                    throw new FormatException("hangar: part " + rec.Key + " has no body header");
                if (!ValidPartId(partId)) throw new FormatException("hangar: bad part id in '" + rec.Key + "'");
                double integ = Float(rec, FieldIntegrity), max = Float(rec, FieldMax);
                if (double.IsNaN(integ) || double.IsInfinity(integ) || double.IsNaN(max) || double.IsInfinity(max) || integ < 0 || integ > max)
                    throw new FormatException("hangar: integrity out of range for " + rec.Key);
                long hits = Int(rec, FieldHits), queue = Int(rec, FieldQueue);
                if (hits < 0 || queue < -1) throw new FormatException("hangar: bad counters for " + rec.Key);
                if (!rec.TryGet(FieldRepaired, out var rep) || rep.Kind != SaveValueKind.Bool) throw new FormatException("hangar: " + rec.Key + " lacks Bool 'repaired'");
                body.Parts.Add(new PartRepairState
                {
                    Damage = new PartDamage { PartId = partId, Integrity = integ, MaxIntegrity = max, LastHitStep = Int(rec, FieldLastHit), Hits = hits },
                    Repaired = rep.AsBool, QueueIndex = queue,
                });
            }
            foreach (var b in bodies.Values) ValidateQueue(b);
            _bodies.Clear();
            foreach (var kv in bodies) _bodies.Add(kv.Key, kv.Value);
        }

        // ---- IHangarService
        public HangarPhase GetPhase(PlayerSlot slot) => _bodies.TryGetValue(slot.Index, out var b) ? b.Phase : HangarPhase.Fight;
        public long GetCycle(PlayerSlot slot) => _bodies.TryGetValue(slot.Index, out var b) ? b.Cycle : 0;

        public bool TryGetParts(PlayerSlot slot, List<PartRepairState> into)
        {
            if (!_bodies.TryGetValue(slot.Index, out var b)) return false;
            into.Clear(); into.AddRange(b.Parts); return true;
        }

        public void EnterHangar(IBodyDamageRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            var incoming = new List<PartDamage>();
            record.CopyParts(incoming);
            if (incoming.Count == 0) throw new ArgumentException("Damage record of " + record.Slot + " lists no parts");
            incoming.Sort((a, c) => string.CompareOrdinal(a.PartId, c.PartId));
            for (int i = 0; i < incoming.Count; i++)
            {
                Validate(incoming[i]);
                if (i > 0 && incoming[i].PartId == incoming[i - 1].PartId) throw new ArgumentException("Duplicate part " + incoming[i].PartId);
            }
            int idx = record.Slot.Index;
            _bodies.TryGetValue(idx, out var prev);
            var next = new Body { Phase = HangarPhase.Hangar, Cycle = prev?.Cycle ?? 0, SiteId = Site.Id };
            foreach (var d in incoming)
            {
                var old = prev == null ? default : FindPart(prev, d.PartId, out var o) ? o : default;
                bool had = prev != null && FindPart(prev, d.PartId, out _);
                bool unchanged = had && old.Damage.Equals(d);
                next.Parts.Add(new PartRepairState
                {
                    Damage = d,
                    // a part hit since the last visit is no longer "repaired"; an untouched one keeps its repair state and queue place
                    Repaired = unchanged && old.Repaired,
                    QueueIndex = unchanged ? old.QueueIndex : -1,
                });
            }
            Compact(next);
            _bodies[idx] = next;
        }

        public void QueueRepair(PlayerSlot slot, string partId)
        {
            var b = InHangar(slot);
            int i = IndexOf(b, partId);
            if (i < 0) throw new ArgumentException("Unknown part " + partId + " on " + slot);
            var p = b.Parts[i];
            if (p.QueueIndex >= 0) return;
            long next = 0;
            foreach (var q in b.Parts) if (q.QueueIndex >= next) next = q.QueueIndex + 1;
            p.QueueIndex = next; b.Parts[i] = p;
        }

        public int QueueAllDamaged(PlayerSlot slot)
        {
            var b = InHangar(slot);
            int n = 0;
            foreach (var p in b.Parts.ToArray())
                if (p.Damage.Integrity < p.Damage.MaxIntegrity && p.QueueIndex < 0) { QueueRepair(slot, p.Damage.PartId); n++; }
            return n;
        }

        public int ProcessRepairs(PlayerSlot slot)
        {
            var b = InHangar(slot);
            int n = 0;
            for (int i = 0; i < b.Parts.Count; i++)
            {
                var p = b.Parts[i];
                if (p.QueueIndex < 0) continue;
                p.Damage.Integrity = p.Damage.MaxIntegrity;
                p.Repaired = true; p.QueueIndex = -1;
                b.Parts[i] = p; n++;
            }
            return n;
        }

        public HangarSpawn LeaveHangar(IBodyDamageRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            var b = InHangar(record.Slot);
            var parts = new List<PartDamage>(b.Parts.Count);
            foreach (var p in b.Parts) parts.Add(p.Damage);
            record.ApplyParts(parts);
            b.Phase = HangarPhase.Fight; b.Cycle++;
            return ResolveSpawn();
        }

        public HangarSpawn ResolveSpawn()
        {
            double x = Site.BayX, z = Site.BayZ; bool anchored = false;
            if (_world != null && Site.AnchorBuilding.IsValid && _world.TryGetBuilding(Site.AnchorBuilding, out var info))
            { x = info.AnchorX + Site.AnchorOffsetX; z = info.AnchorZ + Site.AnchorOffsetZ; anchored = true; }
            double y = _world != null ? _world.SampleTerrain(x, z) : double.NaN;
            bool known = !double.IsNaN(y);
            return new HangarSpawn(Site, x, known ? y : double.NaN, z, Site.HeadingDeg, known, anchored);
        }

        // ---- helpers
        Body InHangar(PlayerSlot slot)
        {
            if (!_bodies.TryGetValue(slot.Index, out var b) || b.Phase != HangarPhase.Hangar)
                throw new InvalidOperationException(slot + " is not in the hangar");
            return b;
        }

        static bool ValidPartId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            foreach (char c in id) if (c == '/' || c < ' ') return false;
            return true;
        }

        static void Validate(PartDamage d)
        {
            if (!ValidPartId(d.PartId)) throw new ArgumentException("Bad part id '" + d.PartId + "'");
            if (double.IsNaN(d.Integrity) || double.IsInfinity(d.Integrity) || double.IsNaN(d.MaxIntegrity) || double.IsInfinity(d.MaxIntegrity)
                || d.Integrity < 0 || d.Integrity > d.MaxIntegrity)
                throw new ArgumentException("Part " + d.PartId + " integrity out of range");
            if (d.Hits < 0) throw new ArgumentException("Part " + d.PartId + " has negative hits");
        }

        static bool FindPart(Body b, string id, out PartRepairState p)
        {
            int i = IndexOf(b, id);
            p = i >= 0 ? b.Parts[i] : default;
            return i >= 0;
        }

        static int IndexOf(Body b, string id)
        {
            for (int i = 0; i < b.Parts.Count; i++) if (string.Equals(b.Parts[i].Damage.PartId, id, StringComparison.Ordinal)) return i;
            return -1;
        }

        /// <summary>Renumbers queue positions to 0..k-1 keeping order.</summary>
        static void Compact(Body b)
        {
            var queued = new List<int>();
            for (int i = 0; i < b.Parts.Count; i++) if (b.Parts[i].QueueIndex >= 0) queued.Add(i);
            queued.Sort((x, y) => b.Parts[x].QueueIndex.CompareTo(b.Parts[y].QueueIndex));
            for (int k = 0; k < queued.Count; k++) { var p = b.Parts[queued[k]]; p.QueueIndex = k; b.Parts[queued[k]] = p; }
        }

        static void ValidateQueue(Body b)
        {
            var seen = new HashSet<long>(); int count = 0;
            foreach (var p in b.Parts) if (p.QueueIndex >= 0) { count++; if (!seen.Add(p.QueueIndex)) throw new FormatException("hangar: duplicate queue index"); }
            for (long k = 0; k < count; k++) if (!seen.Contains(k)) throw new FormatException("hangar: repair queue has a gap");
        }

        static long Int(SaveRecord rec, string f)
        {
            if (!rec.TryGet(f, out var v) || v.Kind != SaveValueKind.Int64) throw new FormatException("hangar: " + rec.Key + " lacks Int64 '" + f + "'");
            return v.AsInt64;
        }
        static double Float(SaveRecord rec, string f)
        {
            if (!rec.TryGet(f, out var v) || v.Kind != SaveValueKind.Float64) throw new FormatException("hangar: " + rec.Key + " lacks Float64 '" + f + "'");
            return v.AsFloat64;
        }
        static string Str(SaveRecord rec, string f)
        {
            if (!rec.TryGet(f, out var v) || v.Kind != SaveValueKind.String) throw new FormatException("hangar: " + rec.Key + " lacks String '" + f + "'");
            return v.AsString;
        }
    }

    public static class HangarModule
    {
        /// <summary>Returns the service it registered, or null when hangar.stub is off (nothing registered). The composition root registers the returned service with SaveSystem.</summary>
        public static HangarService Install(IServiceRegistry registry, IFeatureFlags flags, IGeoWorld world = null, HangarSite site = null)
        {
            if (registry == null || flags == null || !flags.IsOn(HangarFlags.Stub)) return null;
            var service = new HangarService(site ?? HangarSites.CrissyField, world);
            registry.Register<IHangarService>(service);
            return service;
        }
    }
}
