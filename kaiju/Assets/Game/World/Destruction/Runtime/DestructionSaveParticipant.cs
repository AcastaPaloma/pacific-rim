// destruction@v1 save section (F-043, R-021 §5): sparse per-building damage keyed by canonical BuildingId string.
using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.World.Destruction
{
    /// <summary>
    /// Section "destruction", version 1. One record per building with state > 0 or energy > 0 (key = BuildingId.ToString(),
    /// ordinal order is Core's). Fields: state, e_cum_j (Float64 bits), debris_kg (Float64 bits), last_step, cause. Header record
    /// "_meta" carries model_rev. Records of buildings that are not loaded are kept and re-applied by DamageModel.Register.
    /// Read validates every record before changing the model; a bad record throws FormatException and the model is untouched.
    /// </summary>
    public sealed class DestructionSaveParticipant : ISaveParticipant
    {
        public const string Section = "destruction";
        public const int Version = 1;
        public const string MetaKey = "_meta";
        public const string FieldModelRev = "model_rev";
        public const string FieldState = "state";
        public const string FieldEnergy = "e_cum_j";
        public const string FieldDebris = "debris_kg";
        public const string FieldLastStep = "last_step";
        public const string FieldCause = "cause";

        readonly DamageModel _model;

        public DestructionSaveParticipant(DamageModel model) { _model = model ?? throw new ArgumentNullException(nameof(model)); }

        public string SectionName => Section;
        public int SectionVersion => Version;
        /// <summary>model_rev found by the last Read (0 before any Read).</summary>
        public long LoadedModelRevision { get; private set; }

        public void Write(SaveSection section)
        {
            section.Add(MetaKey).Set(FieldModelRev, SaveValue.Of((long)DestructionContractVersions.ModelRevision));
            var list = new List<BuildingDamageState>();
            _model.CopyDamage(list);
            foreach (var s in list)
            {
                section.Add(s.Id.ToString())
                    .Set(FieldState, SaveValue.Of((long)s.State))
                    .Set(FieldEnergy, SaveValue.Of(s.CumulativeEnergyJ))
                    .Set(FieldDebris, SaveValue.Of(s.DebrisKg))
                    .Set(FieldLastStep, SaveValue.Of(s.LastStep))
                    .Set(FieldCause, SaveValue.Of((long)s.Cause));
            }
        }

        public void Read(SaveSection section)
        {
            long rev = 0;
            var list = new List<BuildingDamageState>(section.Records.Count);
            foreach (var rec in section.Records)
            {
                if (rec.Key == MetaKey) { rev = Int(rec, FieldModelRev); continue; }
                if (!BuildingId.TryParse(rec.Key, out var id) || !id.IsValid || id.ToString() != rec.Key)
                    throw new FormatException("destruction: bad building key '" + rec.Key + "'");
                long state = Int(rec, FieldState), cause = Int(rec, FieldCause);
                if (state < 0 || state > (long)DamageState.Complete) throw new FormatException("destruction: state " + state + " out of range for " + rec.Key);
                if (cause < 0 || cause > (long)DamageCause.Debris) throw new FormatException("destruction: cause " + cause + " out of range for " + rec.Key);
                list.Add(new BuildingDamageState
                {
                    Id = id, State = (byte)state, Cause = (byte)cause, LastStep = Int(rec, FieldLastStep),
                    CumulativeEnergyJ = Float(rec, FieldEnergy), DebrisKg = Float(rec, FieldDebris),
                });
            }
            try { _model.Restore(list); }
            catch (ArgumentException ex) { throw new FormatException("destruction: " + ex.Message, ex); }
            LoadedModelRevision = rev;
        }

        static long Int(SaveRecord rec, string field)
        {
            if (!rec.TryGet(field, out var v) || v.Kind != SaveValueKind.Int64) throw new FormatException("destruction: " + rec.Key + " lacks Int64 '" + field + "'");
            return v.AsInt64;
        }

        static double Float(SaveRecord rec, string field)
        {
            if (!rec.TryGet(field, out var v) || v.Kind != SaveValueKind.Float64) throw new FormatException("destruction: " + rec.Key + " lacks Float64 '" + field + "'");
            return v.AsFloat64;
        }
    }
}
