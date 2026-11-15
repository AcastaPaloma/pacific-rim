using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Game.Core;
using Game.Meta.Economy;
using Game.Meta.Refit;

namespace Game.Meta.Refit.Tests
{
    public class RefitTests
    {
        sealed class Seen : IEventHandler<RefitApplied>
        {
            public readonly List<RefitApplied> List = new List<RefitApplied>();
            public void Handle(in RefitApplied e) { List.Add(e); }
        }

        static EntityId Id(ulong v) => EntityId.FromValue(v);

        // ---------------------------------------------------------------- flag, module

        [Test] public void Flag_FileDefaultsOff()
        {
            string t = File.ReadAllText(Path.Combine(Kit.Root(), "Assets", "Game", "Config", "FeatureFlags", "Meta.Refit.json"));
            StringAssert.Contains("\"meta.refit\": false", t);
            var snap = FeatureFlagLoader.Parse(t, "Meta.Refit.json", new CollectingErrorSink());
            Assert.IsFalse(snap.IsOn(RefitFlags.Refit));
        }

        [Test] public void Install_OnlyWhenFlagOnAndArgumentsPresent()
        {
            var rig = Kit.NewRig(); var reg = new ServiceRegistry();
            Assert.IsNull(RefitModule.Install(reg, FeatureFlagSnapshot.AllOff, rig.Parts, rig.Chassis, rig.Pricing, rig.Eco));
            Assert.IsFalse(reg.IsRegistered<IRefitService>());
            var on = new FeatureFlagSnapshot(new[] { new KeyValuePair<string, bool>(RefitFlags.Refit, true) });
            Assert.IsNull(RefitModule.Install(reg, on, rig.Parts, rig.Chassis, rig.Pricing, null));
            Assert.IsNull(RefitModule.Install(reg, on, null, rig.Chassis, rig.Pricing, rig.Eco));
            var svc = RefitModule.Install(reg, on, rig.Parts, rig.Chassis, rig.Pricing, rig.Eco);
            Assert.IsNotNull(svc); Assert.AreSame(svc, reg.Resolve<IRefitService>());
        }

        // ---------------------------------------------------------------- catalogue and R-104 anchors

        [Test] public void Defaults_AreValid_MatchEconomyTonnageAndPrice()
        {
            var rig = Kit.NewRig();
            foreach (var ch in rig.Chassis.All)
            {
                var def = rig.Svc.DefaultBuild(ch.Id);
                var rep = rig.Svc.Validate(ch.Id, def);
                Assert.IsTrue(rep.Ok, ch.Id + ": " + rep.FirstViolation + " " + rep.Detail);
                Assert.AreEqual(14, ch.Slots.Length);
                var byClass = new SortedDictionary<string, long>();
                foreach (var kv in def) foreach (var c in rig.Parts.Get(kv.Value).CompositionKg) { byClass.TryGetValue(c.Key, out long v); byClass[c.Key] = v + c.Value; }
                var econ = rig.Prices.Chassis[ch.Id].Tonnes;
                Assert.AreEqual(econ.Count, byClass.Count, ch.Id);
                foreach (var kv in econ) Assert.AreEqual(kv.Value * 1000, byClass[kv.Key], ch.Id + " " + kv.Key);
                Assert.AreEqual(rig.Prices.ChassisMassTonnes(ch.Id) * 1000, rep.Spec.TotalMassKg);
                Assert.AreEqual(rig.Prices.ChassisPriceUsd(ch.Id), rig.Svc.PriceOf(ch.Id, def), "a default build costs what the economy says the chassis costs");
            }
            Assert.AreEqual(551_000_000, rig.Svc.PriceOf("balanced", rig.Svc.DefaultBuild("balanced")) / 1_000_000 * 1_000_000, 1_000_000);
        }

        [Test] public void Physics_MatchesTheSourcedAnchors()
        {
            var rig = Kit.NewRig();
            var bal = rig.Svc.Validate("balanced", rig.Svc.DefaultBuild("balanced")).Spec; var hv = rig.Svc.Validate("heavy_brawler", rig.Svc.DefaultBuild("heavy_brawler")).Spec;
            Assert.AreEqual(180e6, bal.PowerRatedW, 1.0, "0.2 MW per tonne of 900 t reactor"); Assert.AreEqual(260e6, hv.PowerRatedW, 1.0);
            var ch = rig.Chassis.Get("balanced");
            double walk = ch.Cot * bal.TotalMassKg * 9.81 * ch.WalkSpeedMs;
            Assert.AreEqual(75e6, walk, 1.5e6, "walking power about 75 MW (R-104 section 9)");
            Assert.AreEqual(9990e3, ch.GroundPressurePa * ch.FootAreaM2 / 9.81, 1e3);
            Assert.LessOrEqual(ch.MassCapKg, 9990e3); Assert.LessOrEqual(rig.Chassis.Get("heavy_brawler").MassCapKg, 13558e3);
            Assert.AreEqual(30e6, ch.PassiveHeatW);
            Assert.AreEqual(ch.FrameHeightM * 0.45, 36.0, 1e-9);
            // atan(stance half-width / CoM height): 16.7 and 20.6 degrees in R-104; the model also takes the loss of one arm
            double intactBal = Math.Atan2(12.0 - Math.Abs(bal.ComX), bal.ComY) * 180 / Math.PI;
            Assert.AreEqual(Math.Atan2(12.0, 40.0) * 180 / Math.PI, intactBal, 1.5, "stance 12 m over a CoM near 40 m");
            // shoulder torque about 355 MN m in R-104 (arm 400 t at 35 m, alpha 1)
            var ev = RefitPhysics.Evaluate(ch, ch.DefaultBuild.Select(p => rig.Parts.Get(p)).ToArray());
            int sh = ch.SlotIndex("shoulder_r");
            Assert.AreEqual(355e6, ev.TorqueDemandNm[sh], 0.05 * 355e6, "shoulder demand within 5% of the R-104 number");
            Assert.AreEqual(ev.TorqueDemandNm[sh] * 1.06, ev.TorqueRatedNm[sh], 0.01 * ev.TorqueRatedNm[sh]);
            Assert.AreEqual(ev.TorqueDemandNm[ch.SlotIndex("shoulder_l")], ev.TorqueDemandNm[sh], 1e-6 * ev.TorqueDemandNm[sh], "mirror symmetry");
        }

        [Test] public void Spec_IsDeterministic_SumsMass_AndHashFollowsTheBuild()
        {
            var rig = Kit.NewRig(); var def = rig.Svc.DefaultBuild("balanced");
            Assert.IsTrue(rig.Svc.TryBuild("balanced", def, out var a, out _)); Assert.IsTrue(rig.Svc.TryBuild("balanced", def, out var b, out _));
            Assert.AreEqual(a.Hash, b.Hash); Assert.AreNotEqual(0, a.Hash);
            Assert.AreEqual(a.PartMassKg.Sum(), a.TotalMassKg); Assert.AreEqual(14, a.SlotNames.Length);
            Assert.AreEqual(RefitContractVersions.BuildSpecSchema, a.Schema);
            Assert.IsTrue(a.Inertia[0] > 0 && a.Inertia[1] > 0 && a.Inertia[2] > 0);
            Assert.AreEqual(0.0, a.Inertia[3] / a.Inertia[0], 1e-9, "left-right symmetric build: no xy product");
            Assert.AreEqual(0.0, a.ComX, 1e-9);
            Assert.IsTrue(a.TorqueLimitNm[Array.IndexOf(a.SlotNames, "shoulder_l")] > 0); Assert.AreEqual(0.0, a.TorqueLimitNm[Array.IndexOf(a.SlotNames, "foot_l")]);
            var swapped = Kit.With(def, ("forearm_l", "forearm_blade"), ("forearm_r", "forearm_blade"));
            Assert.IsTrue(rig.Svc.TryBuild("balanced", swapped, out var c, out _)); Assert.AreNotEqual(a.Hash, c.Hash);
            Assert.Less(c.TotalMassKg, a.TotalMassKg);
            // a one-sided arm moves the CoM to that side and left is the mirror of right
            var oneR = Kit.With(def, ("forearm_r", "forearm_blade")); var oneL = Kit.With(def, ("forearm_l", "forearm_blade"));
            Assert.IsTrue(rig.Svc.TryBuild("balanced", oneR, out var r, out _)); Assert.IsTrue(rig.Svc.TryBuild("balanced", oneL, out var l, out _));
            Assert.Less(r.ComX, 0.0, "lighter right arm leaves the CoM on the left"); Assert.AreEqual(-r.ComX, l.ComX, 1e-9);
        }

        [Test] public void Validate_FirstViolationIsTheDocumentedOne_AndMarginsAreReported()
        {
            var rig = Kit.NewRig(); var svc = rig.Svc; var def = svc.DefaultBuild("balanced");
            var rep = svc.Validate("balanced", def);
            CollectionAssert.AreEqual(new[] { RefitViolation.Mass, RefitViolation.Power, RefitViolation.HeatRejection, RefitViolation.HeatStorage, RefitViolation.Torque, RefitViolation.ComHeight,
                                              RefitViolation.ComLateral, RefitViolation.ComForeAft, RefitViolation.TiltMargin }, rep.Margins.Select(m => m.Kind));
            Assert.IsTrue(rep.Margins.All(m => m.Ok && m.Margin >= 0));
            Assert.AreEqual(RefitViolation.Mass, svc.Validate("balanced", Kit.With(def, ("torso", "torso_balanced_plated"))).FirstViolation);
            Assert.AreEqual(RefitViolation.Power, svc.Validate("balanced", Kit.With(def, ("back_mount", "back_overdrive"))).FirstViolation);
            var heat = svc.Validate("balanced", Kit.With(def, ("back_mount", "back_launcher")));
            Assert.AreEqual(RefitViolation.HeatRejection, heat.FirstViolation, "the launcher carries no radiators");
            var tq = svc.Validate("balanced", Kit.With(def, ("back_mount", "back_radiator"), ("forearm_l", "forearm_siege_cannon"), ("forearm_r", "forearm_siege_cannon")));
            Assert.AreEqual(RefitViolation.Torque, tq.FirstViolation); Assert.IsTrue(tq.Subject.StartsWith("shoulder") || tq.Subject.StartsWith("upper_arm"), tq.Subject);
            Assert.Less(tq.Margins.First(m => m.Kind == RefitViolation.Torque).Margin, 0);
            Assert.AreEqual(RefitViolation.UnknownChassis, svc.Validate("tank", def).FirstViolation);
            Assert.AreEqual(RefitViolation.SlotMissing, svc.Validate("balanced", null).FirstViolation);
            Assert.Throws<ArgumentException>(() => svc.DefaultBuild("tank"));
            // the floor chassis is held to the basic catalogue
            var basic = svc.DefaultBuild("basic_balanced");
            Assert.AreEqual(RefitViolation.WrongChassis, svc.Validate("basic_balanced", Kit.With(basic, ("hip_leg_l", "hip_leg_titanium"))).FirstViolation);
        }

        [Test] public void Catalogue_RejectsBadData()
        {
            string parts = Kit.PartsText(), chassis = Kit.ChassisText();
            void BadParts(string from, string to, string why) { Assert.AreNotEqual(parts, parts.Replace(from, to), why); Assert.Throws<FormatException>(() => PartCatalog.Parse(parts.Replace(from, to)), why); }
            BadParts("\"parts@v1\"", "\"parts@v2\"", "schema");
            BadParts("\"torque_bonus_bps\": 1500", "\"torque_bonus_bps\": 1501", "+15% cap");
            BadParts("\"tier\": \"rare\"", "\"tier\": \"standard\"", "rare tier and rare part must agree");
            BadParts("\"slot_type\": \"foot\"", "\"slot_type\": \"tail\"", "unknown slot type");
            BadParts("\"reactor\": 900000", "\"plutonium\": 900000", "unknown class");
            BadParts("\"id\": \"foot_basic\"", "\"id\": \"foot_standard\"", "duplicate part id");
            BadParts("\"radiator_kg\": 110000", "\"radiator_kg\": 110000000", "radiator mass must fit inside the part");
            Assert.Throws<FormatException>(() => PartCatalog.Parse("{"));
            var cat = Kit.Parts();
            void BadChassis(string from, string to, string why) { Assert.AreNotEqual(chassis, chassis.Replace(from, to), why); Assert.Throws<FormatException>(() => ChassisCatalog.Parse(chassis.Replace(from, to), cat), why); }
            BadChassis("\"mass_cap_kg\": 9900000", "\"mass_cap_kg\": 20000000", "mass cap above p A / g");
            BadChassis("\"chassis@v1\"", "\"chassis@v9\"", "schema");
            BadChassis("\"foot_standard\"", "\"foot_heavy\"", "default part must fit the chassis");
            BadChassis("\"parent\": \"torso\"", "\"parent\": \"later_slot\"", "parent must exist earlier");
            BadChassis("\"torque_derate\": 0.8", "\"torque_derate\": 1.5", "derate range");
            Assert.Throws<ArgumentNullException>(() => ChassisCatalog.Parse(chassis, null));
        }

        [Test] public void Pricing_FollowsTheEconomyRule()
        {
            var rig = Kit.NewRig();
            foreach (var p in rig.Parts.All)
            {
                long expect = 0; foreach (var kv in p.CompositionKg) expect += rig.Prices.QuotePart(kv.Key, kv.Value).PriceUsd;
                Assert.AreEqual(expect, rig.Pricing.PartPriceUsd(p), p.Id);
                Assert.Greater(rig.Pricing.PartPriceUsd(p), 0);
            }
            Assert.AreEqual(rig.Prices.SellBackUsd(1_000_001), rig.Pricing.SellBackUsd(1_000_001));
            Assert.AreEqual(400_000, rig.Pricing.SellBackUsd(1_000_000));
            Assert.Throws<ArgumentException>(() => new ClassPricing(new Dictionary<string, long>(), 4000));
            Assert.Throws<ArgumentException>(() => new ClassPricing(new Dictionary<string, long> { { "structure", 0 } }, 4000));
            Assert.Throws<ArgumentException>(() => new ClassPricing(new Dictionary<string, long> { { "structure", 4000 } }, 4000).PartPriceUsd(rig.Parts.Get("foot_standard")), "unpriced class");
            Assert.AreEqual(0, rig.Pricing.SellBackUsd(0));
        }

        // ---------------------------------------------------------------- register, quote, apply

        static (Kit.Rig rig, EntityId mech) Fly(long funds = 5_000_000_000L, string chassis = "balanced", bool issued = false)
        {
            var rig = Kit.NewRig(funds); var m = Id(7);
            rig.Svc.Register(m, Kit.S0, chassis, rig.Svc.DefaultBuild(chassis), issued, 1);
            return (rig, m);
        }

        [Test] public void Register_ChargesNothing_AndRefusesBadBuilds()
        {
            var rig = Kit.NewRig(1000);
            var spec = rig.Svc.Register(Id(1), Kit.S0, "balanced", rig.Svc.DefaultBuild("balanced"), false, 3);
            Assert.AreEqual(1000, rig.Eco.Funds); Assert.IsTrue(rig.Svc.IsRegistered(Id(1)));
            var b = rig.Svc.Get(Id(1)); Assert.AreEqual(spec.Hash, b.SpecHash); Assert.AreEqual(0, b.Refits); Assert.AreEqual("forearm_cannon", b.Parts["forearm_l"]);
            Assert.IsTrue(b.Wear.Values.All(w => w == 0.0)); Assert.IsTrue(b.Issued.Values.All(i => !i)); Assert.AreEqual(Kit.S0, b.Slot);
            Assert.Throws<ArgumentException>(() => rig.Svc.Register(Id(1), Kit.S0, "balanced", rig.Svc.DefaultBuild("balanced"), false, 3), "already registered");
            Assert.Throws<ArgumentException>(() => rig.Svc.Register(Id(2), Kit.S0, "balanced", Kit.With(rig.Svc.DefaultBuild("balanced"), ("torso", "torso_balanced_plated")), false, 3), "invalid build");
            Assert.Throws<ArgumentException>(() => rig.Svc.Register(Id(2), Kit.S0, "balanced", Kit.With(rig.Svc.DefaultBuild("balanced"), ("upper_arm_l", "upper_arm_osteoderm")), false, 3), "rare parts go through Apply");
            Assert.Throws<ArgumentException>(() => rig.Svc.Register(default, Kit.S0, "balanced", rig.Svc.DefaultBuild("balanced"), false, 3));
            Assert.Throws<ArgumentException>(() => rig.Svc.Register(Id(2), default, "balanced", rig.Svc.DefaultBuild("balanced"), false, 3));
            Assert.Throws<ArgumentException>(() => rig.Svc.Register(Id(2), Kit.S0, "tank", rig.Svc.DefaultBuild("balanced"), false, 3));
            Assert.IsNull(rig.Svc.Get(Id(99))); Assert.IsFalse(rig.Svc.IsRegistered(Id(99)));
        }

        [Test] public void Apply_PaysNewPartsMinusSellBack_PublishesOnce_AndResetsWearOnlyWhereChanged()
        {
            var (rig, m) = Fly(); var seen = new Seen(); rig.Bus.Subscribe(seen);
            var def = rig.Svc.DefaultBuild("balanced"); var next = Kit.With(def, ("forearm_l", "forearm_blade"), ("forearm_r", "forearm_blade"));
            rig.Svc.AddWear(m, "forearm_l", 0.4); rig.Svc.AddWear(m, "head", 0.25);
            long pNew = rig.Pricing.PartPriceUsd(rig.Parts.Get("forearm_blade")), pOld = rig.Pricing.PartPriceUsd(rig.Parts.Get("forearm_cannon"));
            long expect = 2 * pNew - 2 * rig.Pricing.SellBackUsd(pOld);
            var q = rig.Svc.Quote(m, next);
            Assert.AreEqual(RefitStatus.Applied, q.Status); Assert.AreEqual(expect, q.CostUsd); Assert.AreEqual(5_000_000_000L, rig.Eco.Funds, "a quote charges nothing"); Assert.AreEqual(0, seen.List.Count);
            var res = rig.Svc.Apply(m, next, 9);
            Assert.AreEqual(RefitStatus.Applied, res.Status); Assert.AreEqual(expect, res.CostUsd); Assert.AreEqual(2, res.SlotsChanged);
            Assert.AreEqual(5_000_000_000L - expect, rig.Eco.Funds);
            var b = rig.Svc.Get(m);
            Assert.AreEqual("forearm_blade", b.Parts["forearm_l"]); Assert.AreEqual(0.0, b.Wear["forearm_l"]); Assert.AreEqual(0.25, b.Wear["head"], "an untouched part keeps its wear");
            Assert.AreEqual(1, b.Refits); Assert.AreEqual(res.Spec.Hash, b.SpecHash); Assert.AreNotEqual(rig.Svc.Validate("balanced", def).Spec.Hash, b.SpecHash);
            Assert.AreEqual(1, seen.List.Count); Assert.AreEqual(m, seen.List[0].Mech); Assert.AreEqual(Kit.S0, seen.List[0].Slot); Assert.AreEqual(expect, seen.List[0].CostUsd);
            Assert.AreEqual(b.SpecHash, seen.List[0].SpecHash); Assert.AreEqual(9, seen.List[0].Step);
            Assert.AreEqual(RefitStatus.NoChange, rig.Svc.Apply(m, next, 10).Status); Assert.AreEqual(1, seen.List.Count, "no second event for an unchanged build");
            // a cheaper build pays out
            var back = rig.Svc.Apply(m, def, 11);
            Assert.AreEqual(RefitStatus.Applied, back.Status);
        }

        [Test] public void Apply_Refusals_ChangeNothing()
        {
            var (rig, m) = Fly(funds: 1_000_000); var seen = new Seen(); rig.Bus.Subscribe(seen);
            var def = rig.Svc.DefaultBuild("balanced"); var before = rig.Svc.Get(m);
            string Snap() { var b = rig.Svc.Get(m); return b.SpecHash + "|" + b.Refits + "|" + string.Join(",", b.Parts.Values) + "|" + string.Join(",", b.Wear.Values) + "|" + rig.Eco.Funds + "|" + seen.List.Count; }
            string s0 = Snap();
            var blades = Kit.With(def, ("upper_arm_l", "upper_arm_titanium"), ("upper_arm_r", "upper_arm_titanium"));
            Assert.AreEqual(RefitStatus.UnknownMech, rig.Svc.Apply(Id(404), blades, 2).Status);
            Assert.AreEqual(RefitStatus.InsufficientFunds, rig.Svc.Apply(m, blades, 2).Status);
            Assert.AreEqual(s0, Snap(), "insufficient funds: nothing changed");
            rig.Gate.Open = false; Assert.AreEqual(RefitStatus.NotAllowed, rig.Svc.Apply(m, blades, 2).Status); rig.Gate.Open = true;
            var bad = rig.Svc.Apply(m, Kit.With(def, ("back_mount", "back_overdrive")), 2);
            Assert.AreEqual(RefitStatus.Invalid, bad.Status); Assert.AreEqual(RefitViolation.Power, bad.Report.FirstViolation);
            Assert.AreEqual(RefitStatus.NoChange, rig.Svc.Apply(m, def, 2).Status);
            var rare = rig.Svc.Apply(m, Kit.With(def, ("upper_arm_l", "upper_arm_osteoderm"), ("upper_arm_r", "upper_arm_osteoderm")), 2);
            Assert.AreEqual(RefitStatus.RareUnavailable, rare.Status);
            Assert.AreEqual(s0, Snap(), "no refusal changed anything");
            Assert.Throws<ArgumentOutOfRangeException>(() => rig.Svc.Apply(m, def, -1));
            Assert.Throws<ArgumentException>(() => rig.Svc.AddWear(Id(404), "head", 0.1)); Assert.Throws<ArgumentException>(() => rig.Svc.AddWear(m, "tail", 0.1));
            Assert.Throws<ArgumentOutOfRangeException>(() => rig.Svc.AddWear(m, "head", -0.1)); Assert.Throws<ArgumentOutOfRangeException>(() => rig.Svc.AddWear(m, "head", double.NaN));
            rig.Svc.AddWear(m, "head", 0.7); rig.Svc.AddWear(m, "head", 0.7); Assert.AreEqual(1.0, rig.Svc.Get(m).Wear["head"], "wear saturates at 1");
        }

        [Test] public void Apply_RareParts_ConsumeSalvageSets_Once()
        {
            var (rig, m) = Fly(); var def = rig.Svc.DefaultBuild("balanced");
            var bone = Kit.With(def, ("upper_arm_l", "upper_arm_osteoderm"), ("upper_arm_r", "upper_arm_osteoderm"));
            var rep = rig.Svc.Validate("balanced", bone); Assert.IsTrue(rep.Ok, rep.FirstViolation + " " + rep.Detail);
            Assert.AreEqual(0.64 * 90, rig.Parts.Get("upper_arm_osteoderm").CompositionKg["armour"] / 1000.0, 0.5, "equal-strength armour mass x 0.64 (R-104 5.4)");
            rig.Salvage.Sets["osteoderm_plate"] = 1;
            Assert.AreEqual(RefitStatus.RareUnavailable, rig.Svc.Apply(m, bone, 3).Status, "two arms need two sets");
            rig.Salvage.Sets["osteoderm_plate"] = 2;
            var one = rig.Svc.Apply(m, Kit.With(def, ("upper_arm_l", "upper_arm_osteoderm")), 4);
            Assert.AreEqual(RefitStatus.Applied, one.Status); Assert.AreEqual(1, one.RareConsumed); Assert.AreEqual(1, rig.Salvage.Sets["osteoderm_plate"]);
            Assert.AreEqual(RefitStatus.NoChange, rig.Svc.Apply(m, Kit.With(def, ("upper_arm_l", "upper_arm_osteoderm")), 5).Status, "keeping a fitted rare part costs no second set");
            Assert.AreEqual(1, rig.Salvage.Sets["osteoderm_plate"]);
            var fibre = Kit.With(def, ("upper_arm_l", "upper_arm_osteoderm"), ("hip_leg_l", "hip_leg_fibre"), ("hip_leg_r", "hip_leg_fibre"));
            var rf = rig.Svc.Validate("balanced", fibre); Assert.IsTrue(rf.Ok, rf.FirstViolation + " " + rf.Detail);
            rig.Salvage.Sets["actuator_fibre"] = 2;
            var rr = rig.Svc.Apply(m, fibre, 6);
            Assert.AreEqual(RefitStatus.Applied, rr.Status); Assert.AreEqual(2, rr.RareConsumed); Assert.AreEqual(0, rig.Salvage.Sets["actuator_fibre"]);
            Assert.Less(rig.Parts.Get("hip_leg_fibre").CompositionKg["drive"], rig.Parts.Get("hip_leg_standard").CompositionKg["drive"], "the +15% torque bonus buys a lighter actuator");
            // swapping back and forth consumes a fresh set each time a rare part is newly fitted
            Assert.AreEqual(RefitStatus.Applied, rig.Svc.Apply(m, def, 7).Status);
            Assert.AreEqual(RefitStatus.RareUnavailable, rig.Svc.Apply(m, fibre, 8).Status);
        }

        [Test] public void BuyAndSellBack_AlwaysLoses_AtLeastSixtyPercentOfThePart()
        {
            var (rig, m) = Fly(); var def = rig.Svc.DefaultBuild("balanced"); long start = rig.Eco.Funds;
            var blades = Kit.With(def, ("forearm_l", "forearm_blade"), ("forearm_r", "forearm_blade"));
            long pA = rig.Pricing.PartPriceUsd(rig.Parts.Get("forearm_cannon")), pB = rig.Pricing.PartPriceUsd(rig.Parts.Get("forearm_blade"));
            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(RefitStatus.Applied, rig.Svc.Apply(m, blades, 10 + 2 * i).Status); Assert.AreEqual(RefitStatus.Applied, rig.Svc.Apply(m, def, 11 + 2 * i).Status);
            }
            long lost = start - rig.Eco.Funds;
            Assert.GreaterOrEqual(lost, 4 * 2 * (long)(0.6 * (pA + pB)) - 8, "each round trip of one slot loses 60% of the two prices");
            Assert.Greater(lost, 0);
        }

        [Test] public void CityIssuedParts_CannotBeSoldBack()
        {
            var (rig, m) = Fly(chassis: "basic_balanced", issued: true);
            var def = rig.Svc.DefaultBuild("basic_balanced"); Assert.AreEqual("forearm_cannon_light", def["forearm_l"]);
            Assert.IsTrue(rig.Svc.Get(m).Issued.Values.All(x => x));
            var blades = Kit.With(def, ("forearm_l", "forearm_blade"));
            long price = rig.Pricing.PartPriceUsd(rig.Parts.Get("forearm_blade"));
            var q = rig.Svc.Quote(m, blades); Assert.AreEqual(RefitStatus.Applied, q.Status); Assert.AreEqual(price, q.CostUsd, "the issued cannon returns nothing");
            Assert.AreEqual(RefitStatus.Applied, rig.Svc.Apply(m, blades, 5).Status);
            var b = rig.Svc.Get(m); Assert.IsFalse(b.Issued["forearm_l"]); Assert.IsTrue(b.Issued["forearm_r"]);
            // now the player owns the blade: selling it back pays 40%
            var back = rig.Svc.Quote(m, def); Assert.AreEqual(rig.Pricing.PartPriceUsd(rig.Parts.Get("forearm_cannon_light")) - rig.Pricing.SellBackUsd(price), back.CostUsd);
            Assert.AreEqual(RefitStatus.Applied, rig.Svc.Apply(m, def, 6).Status);
            Assert.IsFalse(rig.Svc.Get(m).Issued["forearm_l"], "the replacement part is the player's");
        }

        [Test] public void Apply_CanPayOut_WhenTheNewBuildIsCheaper()
        {
            var (rig, m) = Fly(funds: 0);
            var def = rig.Svc.DefaultBuild("balanced");
            var rich = Kit.With(def, ("upper_arm_l", "upper_arm_titanium"), ("upper_arm_r", "upper_arm_titanium"));
            Assert.AreEqual(RefitStatus.InsufficientFunds, rig.Svc.Apply(m, rich, 1).Status);
            rig.Eco.Credit(5_000_000_000L, TransactionReason.Other, 1);
            Assert.AreEqual(RefitStatus.Applied, rig.Svc.Apply(m, rich, 2).Status);
            long f = rig.Eco.Funds;
            var res = rig.Svc.Apply(m, def, 3);
            Assert.AreEqual(RefitStatus.Applied, res.Status);
            Assert.AreEqual(f - res.CostUsd, rig.Eco.Funds);
        }

        [Test] public void MechLimits_AreNotChangedByGate_AndGateSeesSlotAndMech()
        {
            var (rig, m) = Fly();
            var g = new CountingGate(); var svc = new RefitService(rig.Parts, rig.Chassis, rig.Pricing, rig.Eco, null, null, g);
            svc.Register(m, Kit.S1, "balanced", svc.DefaultBuild("balanced"), false, 0);
            svc.Apply(m, svc.DefaultBuild("balanced"), 1);
            Assert.AreEqual(Kit.S1, g.LastSlot); Assert.AreEqual(m, g.LastMech); Assert.AreEqual(1, g.Calls);
        }
        sealed class CountingGate : IRefitGate { public int Calls; public PlayerSlot LastSlot; public EntityId LastMech; public bool CanRefit(PlayerSlot s, EntityId m) { Calls++; LastSlot = s; LastMech = m; return true; } }

        // ---------------------------------------------------------------- save: refit@v1

        static (SaveSystem sys, Kit.Rig rig) Saved()
        {
            var rig = Kit.NewRig(); var sys = new SaveSystem("refit-test"); sys.Register(rig.Svc);
            var def = rig.Svc.DefaultBuild("balanced");
            rig.Svc.Register(Id(11), Kit.S0, "balanced", def, false, 1);
            rig.Svc.Register(Id(12), Kit.S1, "heavy_brawler", rig.Svc.DefaultBuild("heavy_brawler"), false, 2);
            rig.Svc.Register(Id(13), Kit.S1, "basic_balanced", rig.Svc.DefaultBuild("basic_balanced"), true, 3);
            rig.Eco.Credit(5_000_000_000L, TransactionReason.Other, 1);
            rig.Svc.Apply(Id(11), Kit.With(def, ("forearm_l", "forearm_blade"), ("forearm_r", "forearm_blade")), 5);
            rig.Svc.AddWear(Id(11), "head", 0.3); rig.Svc.AddWear(Id(12), "foot_l", 0.0123456789);
            return (sys, rig);
        }

        [Test] public void Save_RoundTripsAssignmentsWearOwnershipAndHash()
        {
            var (sys, a) = Saved();
            var rig2 = Kit.NewRig(); var sys2 = new SaveSystem("refit-test"); sys2.Register(rig2.Svc);
            var doc = sys.Capture(); var res = sys2.Restore(doc);
            Assert.IsTrue(res.Ok, res.Message);
            foreach (ulong id in new ulong[] { 11, 12, 13 })
            {
                var x = a.Svc.Get(Id(id)); var y = rig2.Svc.Get(Id(id));
                Assert.AreEqual(x.SpecHash, y.SpecHash); Assert.AreEqual(x.Refits, y.Refits); Assert.AreEqual(x.Chassis, y.Chassis); Assert.AreEqual(x.Slot, y.Slot);
                CollectionAssert.AreEqual(x.Parts, y.Parts); CollectionAssert.AreEqual(x.Issued, y.Issued);
                CollectionAssert.AreEqual(x.Wear.Values.Select(BitConverter.DoubleToInt64Bits), y.Wear.Values.Select(BitConverter.DoubleToInt64Bits));
            }
            Assert.AreEqual(0.0123456789, rig2.Svc.Get(Id(12)).Wear["foot_l"]);
            Assert.AreEqual(a.Svc.Get(Id(11)).SpecHash, rig2.Svc.Validate("balanced", rig2.Svc.Get(Id(11)).Parts).Spec.Hash);
        }

        [Test] public void Save_CorruptSectionsAreRejectedAndServiceUntouched()
        {
            var (sys, rig) = Saved();
            string Digest() { var d = sys.Capture(); d.TryGetSection("refit", out var s); return string.Join("|", s.Records.Select(r => r.Key + ":" + string.Join(",", r.FieldNames.OrderBy(f => f, StringComparer.Ordinal).Select(f => f + "=" + r.Get(f).Kind + r.Get(f).GetHashCode())))); }
            string before = Digest();
            SaveRecord Head(SaveSection s, string id) => s.Records.First(r => r.Key == "mech:" + id);
            SaveRecord Slot(SaveSection s, string id, string slot) => s.Records.First(r => r.Key == "mech:" + id + "/slot:" + slot);
            const string A = "000000000011", Bh = "000000000012";
            var cases = new List<Action<SaveSection>>
            {
                s => s.Remove("totals"),
                s => { s.TryGet("totals", out var r); r.Set("mechs", SaveValue.Of(9L)); },
                s => { s.TryGet("totals", out var r); r.Set("mechs", SaveValue.Of(-1L)); },
                s => Head(s, A).Set("chassis", SaveValue.Of("tank")),
                s => Head(s, A).Set("chassis", SaveValue.Of(3L)),
                s => Head(s, A).Set("slot", SaveValue.Of(-2L)),
                s => Head(s, A).Set("spec_hash", SaveValue.Of(12345L)),
                s => Head(s, A).Set("refits", SaveValue.Of(-1L)),
                s => Head(s, A).Set("applied_step", SaveValue.Of(0L)),                                     // before registration
                s => Slot(s, A, "head").Set("part_id", SaveValue.Of("head_longrange")),                    // valid part, wrong hash
                s => Slot(s, A, "head").Set("part_id", SaveValue.Of("foot_standard")),                     // wrong slot type
                s => Slot(s, A, "head").Set("part_id", SaveValue.Of("no_such_part")),
                s => Slot(s, A, "head").Set("part_id", SaveValue.Of(7L)),
                s => Slot(s, Bh, "head").Set("part_id", SaveValue.Of("head_sensor")),                      // a balanced part on the heavy chassis
                s => Slot(s, A, "head").Set("wear", SaveValue.Of(1.5)),
                s => Slot(s, A, "head").Set("wear", SaveValue.Of(-0.5)),
                s => Slot(s, A, "head").Set("wear", SaveValue.Of(double.NaN)),
                s => Slot(s, A, "head").Set("wear", SaveValue.Of(3L)),
                s => Slot(s, A, "head").Set("issued", SaveValue.Of(1L)),
                s => s.Remove("mech:" + A + "/slot:head"),
                s => s.Add("mech:" + A + "/slot:tail").Set("part_id", SaveValue.Of("head_sensor")).Set("wear", SaveValue.Of(0.0)).Set("issued", SaveValue.Of(false)),
                s => s.Add("mech:000000000099/slot:head").Set("part_id", SaveValue.Of("head_sensor")).Set("wear", SaveValue.Of(0.0)).Set("issued", SaveValue.Of(false)),
                s => s.Add("mech:000000000099").Set("slot", SaveValue.Of(0L)),
                s => s.Add("mech:0000000000x1"),
                s => s.Add("mystery:1"),
                s => s.Add("mech:000000000012/bad:head"),
            };
            int n = 0;
            foreach (var edit in cases)
            {
                var good = sys.Capture(); edit(good.Sections.First(x => x.Name == "refit"));
                var res = sys.Restore(good);
                Assert.AreEqual(SaveLoadStatus.Corrupt, res.Status, "case " + n + ": " + res.Message);
                Assert.AreEqual(before, Digest(), "service untouched after case " + n);
                n++;
            }
        }

        static string Show(SaveValue v) => v.Kind == SaveValueKind.String ? v.AsString : v.Kind == SaveValueKind.Float64 ? v.Float64Bits.ToString() : v.Kind == SaveValueKind.Int64 ? v.AsInt64.ToString() : (v.AsBool ? "1" : "0");

        [Test] public void SameOperations_GiveTheSameSaveDocument()
        {
            string Run()
            {
                var (sys, rig) = Saved();
                rig.Svc.Apply(Id(12), Kit.With(rig.Svc.DefaultBuild("heavy_brawler"), ("forearm_l", "forearm_cannon_heavy")), 9);
                var d = sys.Capture(); d.TryGetSection("refit", out var s);
                return string.Join(";", s.Records.Select(r => r.Key + ":" + string.Join(",", r.FieldNames.OrderBy(f => f, StringComparer.Ordinal).Select(f => f + "=" + r.Get(f).Kind + ":" + Show(r.Get(f))))));
            }
            Assert.AreEqual(Run(), Run());
        }
    }
}
