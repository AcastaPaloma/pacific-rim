using System.Collections.Generic;
using Game.Composition;
using Game.Core;
using Game.Hangar;
using Game.Units.Placeholder;
using NUnit.Framework;

namespace Game.Composition.Tests
{
    /// <summary>T-030c: PlaceholderDamageRecord through IBodyDamageRecord into the Hangar stub and back. Engine-free (dotnet and Unity).</summary>
    public class PlaceholderDamageAdapterTests
    {
        private static readonly PlayerSlot Slot = PlayerSlot.FromIndex(1);
        private static PlaceholderDamageAdapter Make(float max = 900f, long step = 0, System.Func<long> clock = null) =>
            new PlaceholderDamageAdapter(new PlaceholderDamageRecord(EntityId.FromValue(7), max), Slot, clock);

        [Test]
        public void CopyParts_ReportsOneBodyPart_WithRecordNumbers()
        {
            long step = 0;
            var a = Make(900f, 0, () => step);
            a.Record.ApplyDamage(250f); step = 42;
            var parts = new List<PartDamage>();
            a.CopyParts(parts);
            Assert.AreEqual(1, parts.Count);
            Assert.AreEqual("body", parts[0].PartId);
            Assert.AreEqual(650.0, parts[0].Integrity); Assert.AreEqual(900.0, parts[0].MaxIntegrity);
            Assert.AreEqual(1, parts[0].Hits); Assert.AreEqual(42, parts[0].LastHitStep);
            Assert.AreEqual(Slot, a.Slot);
        }

        [Test]
        public void ApplyParts_WritesBackAndIgnoresUnknownParts()
        {
            var a = Make();
            a.ApplyParts(new[] { new PartDamage { PartId = "leg", Integrity = 1, MaxIntegrity = 2 }, new PartDamage { PartId = "body", Integrity = 300, MaxIntegrity = 900, Hits = 3, LastHitStep = 9 } });
            Assert.AreEqual(300f, a.Record.HitPoints); Assert.AreEqual(3, a.Record.HitsTaken);
            a.ApplyParts(new[] { new PartDamage { PartId = "leg", Integrity = 5, MaxIntegrity = 5 } });
            Assert.AreEqual(300f, a.Record.HitPoints, "no body part: unchanged");
        }

        [Test]
        public void FightHangarFight_DamageIsCarriedUntilRepaired_ThreeCycles_Exact()
        {
            var svc = new HangarService(HangarSites.CrissyField);
            var a = Make(900f);
            a.Record.ApplyDamage(123.5f);
            float hp = a.Record.HitPoints; int hits = a.Record.HitsTaken;
            for (int cycle = 1; cycle <= 3; cycle++)
            {
                svc.EnterHangar(a);
                Assert.AreEqual(HangarPhase.Hangar, svc.GetPhase(Slot));
                // leave without repair: the damage comes back bit-exact
                svc.LeaveHangar(a);
                Assert.AreEqual(hp, a.Record.HitPoints); Assert.AreEqual(hits, a.Record.HitsTaken);
                Assert.AreEqual(cycle, svc.GetCycle(Slot));
            }
            svc.EnterHangar(a);
            Assert.AreEqual(1, svc.QueueAllDamaged(Slot));
            Assert.AreEqual(1, svc.ProcessRepairs(Slot));
            svc.LeaveHangar(a);
            Assert.AreEqual(900f, a.Record.HitPoints);
            Assert.AreEqual(PlaceholderDamageState.Intact, a.Record.State);
            Assert.AreEqual(hits, a.Record.HitsTaken, "hits taken are history, not repaired away");
        }
    }
}
