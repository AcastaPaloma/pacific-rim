using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Game.Core;
using Game.Units.Kaiju;
using Game.Units.Kaiju.Contracts;

namespace Game.Units.Kaiju.Tests
{
    /// <summary>M-103 mirror of Tools/kaiju_spec_check.py (T-112): positive run, R-102 numbers, mutation controls, loader and flag behaviour.</summary>
    public class KaijuSpeciesTests
    {
        static readonly string[] Names = { "strider", "breacher", "bastion" };

        static string Root()
        {
            var d = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "Assets", "Game", "Config", "FeatureFlags", "Units.Kaiju.json"))) d = d.Parent;
            Assert.IsNotNull(d, "repo root not found");
            return d.FullName;
        }
        static string Dir() => Path.Combine(Root(), "Assets", "Game", "Units", "Kaiju", "species");
        static KaijuSpeciesSpec Load(string n) => KaijuSpeciesLoader.Parse(File.ReadAllText(Path.Combine(Dir(), n + ".json")));
        static KaijuTissue Tissue() => KaijuSpeciesLoader.ParseTissue(File.ReadAllText(Path.Combine(Dir(), "kaiju_tissue_v1.json")));
        static List<KaijuSpeciesSpec> All() { var l = new List<KaijuSpeciesSpec>(); foreach (var n in Names) l.Add(Load(n)); return l; }

        [Test] public void M103_AllSpeciesPass()
        {
            var r = SpeciesValidator.CheckAll(All(), Tissue());
            var bad = r.FindAll(x => !x.Ok);
            Assert.IsEmpty(bad, string.Join("\n", bad));
            Assert.AreEqual(3 * 9 + 2, r.Count);
            Assert.IsTrue(SpeciesValidator.Passed(r));
        }

        [Test] public void R102_Numbers()
        {
            var d = new Dictionary<string, KaijuSpeciesSpec>(); foreach (var s in All()) d[s.Id] = s;
            var mass = new[] { ("strider@1", 4190.0), ("breacher@1", 35000.0), ("bastion@1", 163900.0) };
            foreach (var (id, t) in mass) Assert.AreEqual(t, SpeciesPhysics.Volume(d[id]) * d[id].Mass.RhoEff / 1e3, t * 0.01, id);
            var sf = new[] { ("strider@1", 2.1), ("breacher@1", 4.2), ("bastion@1", 2.5) };
            foreach (var (id, e) in sf)
            {
                var s = d[id];
                var lc = SpeciesPhysics.Limb(s, s.Mass.MassKg, 0.30, s.Gait.FrChargeCap);
                Assert.AreEqual(e, 600e6 / lc.SigmaBone, 0.06, id);
            }
            var b = d["bastion@1"];
            Assert.AreEqual(0.76, SpeciesPhysics.Limb(b, b.Mass.MassKg, 0.30, 0.15).SigmaMuscle / 6e6, 0.01);
            var st = new[] { ("strider@1", 5.1), ("breacher@1", 6.8), ("bastion@1", 11.2) };
            foreach (var (id, t) in st) Assert.AreEqual(t, d[id].Gait.StridePeriodCruiseS, 0.06, id);
        }

        static void Fails(Action<KaijuSpeciesSpec> mutate, string measure)
        {
            var specs = All(); mutate(specs[0]);
            var r = SpeciesValidator.CheckAll(specs, Tissue());
            Assert.IsFalse(SpeciesValidator.Passed(r));
            Assert.IsFalse(r.Find(x => x.Subject == specs[0].Id && x.Measure == measure).Ok, measure);
        }

        [Test] public void Mutation_MassOff() => Fails(s => s.Mass.MassKg.Value *= 1.05, "M-103.1");
        [Test] public void Mutation_HeightOutOfClass() => Fails(s => s.Geometry.HeightM.Value = 80, "M-103.1");
        [Test] public void Mutation_ThinBone() => Fails(s => s.LimbModel.BoneAreaShare.Value = 0.05, "M-103.3");
        [Test] public void Mutation_ChargeCapTooHigh() => Fails(s => s.Gait.FrChargeCap.Value = 1.5, "M-103.3");
        [Test] public void Mutation_LoweredThreshold() => Fails(s => s.Material.BoneSfChargeMin.Value = 1.0, "M-103.3");
        [Test] public void Mutation_TissueDiffers() => Fails(s => s.Material.BoneYieldPa.Value = 700e6, "M-103.5");
        [Test] public void Mutation_AssumedNotListed() => Fails(s => s.Provenance.AssumedFields = new string[0], "M-103.5");
        [Test] public void Mutation_CruiseOutOfBand() => Fails(s => s.Gait.FrCruise.Value = 0.5, "M-103.6");
        [Test] public void Mutation_StrideOff() => Fails(s => s.Gait.StridePeriodCruiseS.Value = 7.0, "M-103.6");
        [Test] public void Mutation_AttackOverBound() => Fails(s => s.Attacks[0].ECapJ.Value = 1e9, "M-103.7");
        [Test] public void Mutation_GravityAttack() => Fails(s => s.Attacks[3].ECapJ.Value = 1e9, "M-103.7");
        [Test] public void Mutation_FloatsFlag() => Fails(s => s.Water.Floats.Value = 0, "M-103.8");
        [Test] public void Mutation_PressureOverCap() => Fails(s => s.Geometry.FootAreaM2.Value = 5, "M-103.9");

        [Test] public void Mutation_CrossSpeciesRatio()
        {
            var specs = All();
            specs[1].Mass.MassKg.Value *= 10;
            Assert.IsFalse(SpeciesValidator.CheckCross(specs).TrueForAll(x => x.Ok));
        }

        [Test] public void Loader_RejectsBadInput()
        {
            string t = File.ReadAllText(Path.Combine(Dir(), "strider.json"));
            Assert.Throws<FormatException>(() => KaijuSpeciesLoader.Parse(t.Replace("KaijuSpeciesSpec@v1", "KaijuSpeciesSpec@v2")));
            Assert.Throws<FormatException>(() => KaijuSpeciesLoader.Parse(t.Replace("\"Assumed\"", "\"Guess\"")));
            Assert.Throws<FormatException>(() => KaijuSpeciesLoader.Parse(t.Replace("\"hip_height_m\"", "\"hip_h\"")));
            Assert.Throws<FormatException>(() => KaijuSpeciesLoader.Parse("[]"));
            Assert.Throws<FormatException>(() => KaijuSpeciesLoader.Parse("{"));
        }

        [Test] public void EveryNumberHasSourceAndStatus_AssumedShareIsFlagged()
        {
            foreach (var s in All())
            {
                Assert.AreEqual(SpecStatus.Assumed, s.Material.BoneYieldPa.Status);
                Assert.AreEqual(SpecStatus.Derived, s.Mass.MassKg.Status);
                Assert.AreEqual(SpecStatus.Sourced, s.Gait.StrideK.Status);
                Assert.IsNotEmpty(s.Geometry.HipHeightM.Source);
            }
        }

        [Test] public void Flag_FileDefaultsOff_InstallOnlyWhenOn()
        {
            string t = File.ReadAllText(Path.Combine(Root(), "Assets", "Game", "Config", "FeatureFlags", "Units.Kaiju.json"));
            StringAssert.Contains("\"units.kaiju.species\": false", t);
            var snap = FeatureFlagLoader.Parse(t, "Units.Kaiju.json", new CollectingErrorSink());
            Assert.IsFalse(snap.IsOn(KaijuFlags.Species));
            var reg = new ServiceRegistry();
            Assert.IsNull(KaijuModule.Install(reg, snap, All(), Tissue()));
            Assert.IsFalse(reg.IsRegistered<IKaijuSpeciesCatalog>());
            var on = new FeatureFlagSnapshot(new[] { new KeyValuePair<string, bool>(KaijuFlags.Species, true) });
            var c = KaijuModule.Install(reg, on, All(), Tissue());
            Assert.IsNotNull(c);
            Assert.AreSame(c, reg.Resolve<IKaijuSpeciesCatalog>());
            KaijuSpeciesSpec s;
            Assert.IsTrue(c.TryGet("bastion@1", out s));
            Assert.IsFalse(c.TryGet("nope", out s));
            Assert.Throws<FormatException>(() => new KaijuSpeciesCatalog(new[] { Load("strider"), Load("strider") }, Tissue()));
        }
    }
}
