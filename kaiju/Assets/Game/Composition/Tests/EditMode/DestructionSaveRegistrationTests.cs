using System.Collections.Generic;
using Game.Composition;
using Game.Core;
using Game.World.Destruction;
using NUnit.Framework;

namespace Game.Composition.Tests
{
    /// <summary>DEF-M1save-6: the destruction save section is registered exactly when Destruction is installed. Engine-free (dotnet and Unity).</summary>
    public class DestructionSaveRegistrationTests
    {
        private static FeatureFlagSnapshot Flags(bool destruction) =>
            new FeatureFlagSnapshot(new List<KeyValuePair<string, bool>> { new KeyValuePair<string, bool>("destruction.enabled", destruction) });

        private static BuildingId Id(int i) => BuildingId.From(BuildingSource.OsmWay, (ulong)(1000 + i * 7919));
        private static BuildingAttributes Attr() => new BuildingAttributes { HeightM = 40, Levels = 0, FootprintM2 = 400, Residential = false };

        private static DamageModel Damaged()
        {
            var m = new DamageModel();
            for (int i = 0; i < 4; i++) m.Register(Id(i), Attr());
            m.TryGetThresholds(Id(1), out _, out _, out _, out double s4);
            m.Apply(Id(1), s4 * 0.5, DamageCause.Shaking, default, 321);
            m.Apply(Id(3), s4 * 0.3, DamageCause.Shaking, default, 400);
            return m;
        }

        private static List<BuildingDamageState> All(DamageModel m)
        {
            var l = new List<BuildingDamageState>();
            m.CopyDamage(l);
            l.Sort((a, b) => string.CompareOrdinal(a.Id.ToString(), b.Id.ToString()));
            return l;
        }

        [Test]
        public void FlagOn_RegistersParticipant_AndRoundTripPreservesState()
        {
            var reg = new ServiceRegistry();
            var model = DestructionModule.Install(reg, Flags(true));
            Assert.That(model, Is.Not.Null);
            var saves = new SaveSystem("test");
            var participant = DestructionSaveInstaller.Install(model, saves);
            Assert.That(participant, Is.Not.Null);

            // Duplicate ownership of the section proves the participant is registered.
            Assert.Throws<System.ArgumentException>(() => saves.Register(new DestructionSaveParticipant(model)));

            for (int i = 0; i < 4; i++) model.Register(Id(i), Attr());
            model.TryGetThresholds(Id(1), out _, out _, out _, out double s4);
            model.Apply(Id(1), s4 * 0.5, DamageCause.Shaking, default, 321);
            var before = All(model);
            Assert.That(before.Count, Is.GreaterThan(0));

            var doc = saves.Capture();
            Assert.That(doc.TryGetSection(DestructionSaveParticipant.Section, out _), Is.True);
            var loaded = SaveCodec.Deserialize(SaveCodec.Serialize(doc), "mem");
            Assert.That(loaded.Ok, Is.True, loaded.Message);

            var fresh = new DamageModel();
            for (int i = 0; i < 4; i++) fresh.Register(Id(i), Attr());
            var saves2 = new SaveSystem("test");
            DestructionSaveInstaller.Install(fresh, saves2);
            Assert.That(saves2.Restore(loaded.Document).Ok, Is.True);

            var after = All(fresh);
            Assert.That(after.Count, Is.EqualTo(before.Count));
            for (int i = 0; i < before.Count; i++)
            {
                Assert.That(after[i].Id, Is.EqualTo(before[i].Id));
                Assert.That(after[i].State, Is.EqualTo(before[i].State));
                Assert.That(after[i].Cause, Is.EqualTo(before[i].Cause));
                Assert.That(after[i].LastStep, Is.EqualTo(before[i].LastStep));
                Assert.That(after[i].CumulativeEnergyJ, Is.EqualTo(before[i].CumulativeEnergyJ));
                Assert.That(after[i].DebrisKg, Is.EqualTo(before[i].DebrisKg));
            }
        }

        [Test]
        public void SavedDamage_IsReappliedToBuildingsRegisteredAfterRestore()
        {
            var src = Damaged();
            var saves = new SaveSystem("test");
            DestructionSaveInstaller.Install(src, saves);
            var text = SaveCodec.Serialize(saves.Capture());

            var fresh = new DamageModel();
            var saves2 = new SaveSystem("test");
            DestructionSaveInstaller.Install(fresh, saves2);
            Assert.That(saves2.Restore(SaveCodec.Deserialize(text, "mem").Document).Ok, Is.True);
            for (int i = 0; i < 4; i++) fresh.Register(Id(i), Attr());
            var a = All(src); var b = All(fresh);
            Assert.That(b.Count, Is.EqualTo(a.Count));
            for (int i = 0; i < a.Count; i++) { Assert.That(b[i].State, Is.EqualTo(a[i].State)); Assert.That(b[i].CumulativeEnergyJ, Is.EqualTo(a[i].CumulativeEnergyJ)); }
        }

        [Test]
        public void FlagOff_RegistersNothing()
        {
            var reg = new ServiceRegistry();
            var model = DestructionModule.Install(reg, Flags(false));
            Assert.That(model, Is.Null);
            var saves = new SaveSystem("test");
            Assert.That(DestructionSaveInstaller.Install(model, saves), Is.Null);
            Assert.That(saves.Capture().TryGetSection(DestructionSaveParticipant.Section, out _), Is.False);
            // The section name is still free: nothing owns it.
            Assert.DoesNotThrow(() => saves.Register(new DestructionSaveParticipant(new DamageModel())));
        }

        [Test]
        public void NoSaveSystem_RegistersNothing()
        {
            Assert.That(DestructionSaveInstaller.Install(new DamageModel(), null), Is.Null);
        }
    }
}
