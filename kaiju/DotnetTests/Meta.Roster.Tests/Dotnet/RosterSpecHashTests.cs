// T-122: Roster.SetSpecHash (the refit's new MechBuildSpec hash reaches the roster record). New file; RosterTests.cs is untouched.
using System;
using NUnit.Framework;
using Game.Core;
using Game.Meta.Roster;

namespace Game.Meta.Roster.Tests
{
    public class RosterSpecHashTests
    {
        [Test]
        public void SetSpecHash_UpdatesRecord_AndSurvivesSaveRoundTrip()
        {
            var svc = Kit.New(out _);
            var m = svc.Create(Kit.S0, "balanced", 11, 0);
            svc.SetSpecHash(m.MechId, 424242);
            Assert.That(svc.Get(m.MechId).SpecHash, Is.EqualTo(424242));

            var sys = new SaveSystem("hash");
            sys.Register(svc);
            var doc = sys.Capture();
            var twin = Kit.New(out _);
            var sys2 = new SaveSystem("hash");
            sys2.Register(twin);
            var r = sys2.Restore(doc);
            Assert.That(r.Ok, Is.True, r.Message);
            Assert.That(twin.Get(m.MechId).SpecHash, Is.EqualTo(424242));
        }

        [Test]
        public void SetSpecHash_UnknownMech_Throws()
        {
            var svc = Kit.New(out _);
            Assert.Throws<ArgumentException>(() => svc.SetSpecHash(EntityId.FromValue(999), 1));
        }

        [Test]
        public void SetSpecHash_OnLostMech_IsIgnored()
        {
            var svc = Kit.New(out _);
            var m = svc.Create(Kit.S0, "balanced", 5, 0);
            svc.OnFightEnd(Kit.Body(Kit.S0, 0), 10);
            Assert.That(svc.Get(m.MechId).State, Is.EqualTo(MechState.Lost));
            svc.SetSpecHash(m.MechId, 77);
            Assert.That(svc.Get(m.MechId).SpecHash, Is.EqualTo(5));
        }
    }
}
