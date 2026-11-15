using System;
using System.Collections.Generic;
using System.IO;
using Game.Audio;
using Game.Audio.SliceBridge;
using Game.Combat;
using Game.Core;
using Game.World.Destruction;
using Game.World.Seismic;
using NUnit.Framework;

namespace Game.Audio.SliceBridge.Tests
{
    sealed class FakeBus : IEventBus
    {
        readonly Dictionary<Type, List<object>> _h = new Dictionary<Type, List<object>>();
        public int HandlerExceptionCount => 0;
        public void Subscribe<T>(IEventHandler<T> h) where T : unmanaged, IEvent { if (!_h.TryGetValue(typeof(T), out var l)) _h[typeof(T)] = l = new List<object>(); l.Add(h); }
        public bool Unsubscribe<T>(IEventHandler<T> h) where T : unmanaged, IEvent => _h.TryGetValue(typeof(T), out var l) && l.Remove(h);
        public void Publish<T>(in T e) where T : unmanaged, IEvent { if (_h.TryGetValue(typeof(T), out var l)) foreach (IEventHandler<T> h in l.ToArray()) h.Handle(e); }
        public int SubscriberCount<T>() where T : unmanaged, IEvent => _h.TryGetValue(typeof(T), out var l) ? l.Count : 0;
    }

    [TestFixture]
    public class MapperTests
    {
        static readonly BuildingId B1 = BuildingId.From(BuildingSource.OsmWay, 42);

        static SliceAudioBridge Make(bool known = true)
            => new SliceAudioBridge((BuildingId id, out double x, out double y, out double z) => { x = 100; y = 10; z = 200; return known; });

        static List<AudioEvent> Drain(SliceAudioBridge b) { var l = new List<AudioEvent>(); b.Drain(l); return l; }

        [Test]
        public void BuildingDamaged_StateChangeMakesFailureAndDebris()
        {
            var b = Make();
            b.Handle(new BuildingDamaged { BuildingId = B1, State = 3, PrevState = 1, Cause = (byte)DamageCause.Impact, EnergyJ = 1e9f, DebrisKg = 5000f });
            var ev = Drain(b);
            Assert.AreEqual(2, ev.Count);
            Assert.AreEqual(AudioEventKind.StructureFailure, ev[0].Kind);
            Assert.AreEqual(AudioEventKind.DebrisRain, ev[1].Kind);
            Assert.AreEqual(100, ev[0].X); Assert.AreEqual(200, ev[0].Z);
            Assert.AreEqual(AudioMaterial.Concrete, ev[0].Material);
            Assert.Greater(ev[1].EnergyJ, 5000 * 9.81 * 19);
        }

        [Test]
        public void BuildingDamaged_FiltersNonIncreaseAndSlightShaking()
        {
            var b = Make();
            b.Handle(new BuildingDamaged { BuildingId = B1, State = 2, PrevState = 2, Cause = 0, EnergyJ = 1e6f });
            b.Handle(new BuildingDamaged { BuildingId = B1, State = 1, PrevState = 0, Cause = (byte)DamageCause.Shaking, EnergyJ = 1e6f });
            Assert.AreEqual(0, Drain(b).Count);
            Assert.AreEqual(2, b.Stats.DroppedFiltered);
            b.Handle(new BuildingDamaged { BuildingId = B1, State = 1, PrevState = 0, Cause = (byte)DamageCause.Impact, EnergyJ = 1e6f });
            Assert.AreEqual(1, Drain(b).Count);
        }

        [Test]
        public void BuildingDamaged_UnknownBuildingIsCountedNotThrown()
        {
            var b = Make(false);
            b.Handle(new BuildingDamaged { BuildingId = B1, State = 4, PrevState = 3, Cause = 1, EnergyJ = 1e9f });
            Assert.AreEqual(0, Drain(b).Count); Assert.AreEqual(1, b.Stats.DroppedNoPosition);
        }

        [Test]
        public void QuakeBegan_MakesPSRumbleAtHypocentre()
        {
            var b = Make();
            b.Handle(new QuakeBegan { EventId = 1, Mw = 6.9f, HypoX = 5000, HypoZ = 7000, HypoDepthM = 8000, Seed = 9 });
            var ev = Drain(b);
            Assert.AreEqual(3, ev.Count);
            CollectionAssert.AreEqual(new[] { AudioEventKind.QuakeP, AudioEventKind.QuakeS, AudioEventKind.QuakeRumble }, ev.ConvertAll(e => e.Kind));
            Assert.AreEqual(-8000, ev[0].Y); Assert.AreEqual(5000, ev[1].X);
            Assert.Greater(ev[1].EnergyJ, ev[0].EnergyJ);
            Assert.AreEqual(Math.Pow(10, 1.5 * 6.9f + 4.8), ev[2].EnergyJ, ev[2].EnergyJ * 1e-6);
        }

        [Test]
        public void QuakeEnergyGrowsByTenToTheOnePointFivePerMagnitude()
        {
            var b = Make();
            b.Handle(new QuakeBegan { Mw = 5f }); b.Handle(new QuakeBegan { Mw = 6f });
            var ev = Drain(b);
            Assert.AreEqual(Math.Pow(10, 1.5), ev[5].EnergyJ / ev[2].EnergyJ, 1e-6 * Math.Pow(10, 1.5));
        }

        [Test]
        public void WeaponFired_MakesMuzzleBlastWithKineticEnergy()
        {
            var b = Make();
            b.Handle(new WeaponFired { Step = 77, MuzzleX = 1, MuzzleY = 2, MuzzleZ = 3, ProjectileMassKg = 500 });
            var ev = Drain(b);
            Assert.AreEqual(1, ev.Count);
            Assert.AreEqual(AudioEventKind.MuzzleBlast, ev[0].Kind); Assert.AreEqual(77, ev[0].Tick);
            Assert.AreEqual(0.5 * 500 * 1000 * 1000, ev[0].EnergyJ, 1.0);
        }

        [Test]
        public void ImpactImpulse_DerivesMassFromImpulseAndEnergy_AndMaterialFromTarget()
        {
            var b = Make();
            b.Handle(new ImpactImpulse { Step = 5, TargetKind = (byte)TargetKind.Building, PointX = 1, PointY = 2, PointZ = 3, ImpulseNs = 2e6, EnergyJ = 1e8 });
            b.Handle(new ImpactImpulse { Step = 6, TargetKind = (byte)TargetKind.Mech, ImpulseNs = 2e6, EnergyJ = 1e8 });
            b.Handle(new ImpactImpulse { Step = 7, TargetKind = (byte)TargetKind.Ground, EnergyJ = 0 });
            var ev = Drain(b);
            Assert.AreEqual(2, ev.Count);
            Assert.AreEqual(2e6 * 2e6 / (2.0 * 1e8), ev[0].MassKg, 1e-6);
            Assert.AreEqual(AudioMaterial.Concrete, ev[0].Material); Assert.AreEqual(AudioMaterial.Steel, ev[1].Material);
            Assert.AreEqual(1, b.Stats.DroppedFiltered);
        }

        [Test]
        public void EmergencePoint_MakesRockImpact()
        {
            var b = Make();
            b.Handle(new EmergencePoint { EventId = 3, X = 10, Z = 20 });
            var ev = Drain(b);
            Assert.AreEqual(AudioEventKind.Impact, ev[0].Kind); Assert.AreEqual(AudioMaterial.Rock, ev[0].Material);
        }

        [Test]
        public void Drain_CapKeepsAlwaysKindsAndStrongest_InPublicationOrder()
        {
            var b = Make();
            b.Handle(new QuakeBegan { Mw = 6f });
            for (int i = 0; i < 40; i++) b.Handle(new BuildingDamaged { BuildingId = B1, State = 3, PrevState = 2, Cause = 1, EnergyJ = 1e6f * (i + 1) });
            var ev = Drain(b);
            Assert.AreEqual(12, ev.Count);
            Assert.AreEqual(AudioEventKind.QuakeP, ev[0].Kind);
            int fail = 0; double minE = double.MaxValue;
            foreach (var e in ev) if (e.Kind == AudioEventKind.StructureFailure) { fail++; minE = Math.Min(minE, e.EnergyJ); }
            Assert.AreEqual(9, fail);
            Assert.GreaterOrEqual(minE, 32e6);        // the 9 strongest of 40 (32..40 e6)
            Assert.AreEqual(31, b.Stats.DroppedCap);
            for (int i = 1; i < ev.Count; i++) Assert.Greater(ev[i].Id, ev[i - 1].Id);
            Assert.AreEqual(0, b.Pending);
        }

        [Test]
        public void Subscribe_ReceivesBusEvents_UnsubscribeStops()
        {
            var bus = new FakeBus(); var b = Make();
            b.Subscribe(bus);
            Assert.AreEqual(1, bus.SubscriberCount<BuildingDamaged>()); Assert.AreEqual(1, bus.SubscriberCount<QuakeBegan>());
            bus.Publish(new WeaponFired { ProjectileMassKg = 10 });
            Assert.AreEqual(1, b.Pending);
            b.Unsubscribe();
            bus.Publish(new WeaponFired { ProjectileMassKg = 10 });
            Assert.AreEqual(1, b.Pending); Assert.AreEqual(0, bus.SubscriberCount<WeaponFired>());
        }

        [Test]
        public void FootContactHookup_UsesImpulseAndSurface()
        {
            var b = Make();
            b.OnFootContact(10, 3, 1, 0, 2, 1e7, 2.0, 3e5, AudioSurface.Pavement);
            var ev = Drain(b);
            Assert.AreEqual(AudioEventKind.FootContact, ev[0].Kind); Assert.AreEqual(AudioMaterial.Asphalt, ev[0].Material);
            Assert.AreEqual(1e7, ev[0].EnergyJ, 1.0);      // impulse * v / 2
        }

        [Test]
        public void StepCadence_EmitsEveryHalfStrideAndAlternatesFeet()
        {
            var c = new PlaceholderStepCadence(new SliceAudioAssumptions());
            var feet = new List<int>(); double x = 0; double dt = 1.0 / 60.0;
            for (int i = 0; i < 60 * 10; i++) { x += 12.0 * dt; if (c.Step(x, 0, dt, out _, out _, out int f)) feet.Add(f); }
            Assert.That(feet.Count, Is.InRange(5, 7));            // 120 m / 20 m
            for (int i = 1; i < feet.Count; i++) Assert.AreNotEqual(feet[i - 1], feet[i]);
        }

        [Test]
        public void StepCadence_StandingStillIsSilent()
        {
            var c = new PlaceholderStepCadence(new SliceAudioAssumptions());
            for (int i = 0; i < 600; i++) Assert.IsFalse(c.Step(5, 5, 1.0 / 60.0, out _, out _, out _));
            Assert.AreEqual(0, c.Footfalls);
        }
    }

    [TestFixture]
    public class EngineTests
    {
        static AudioEvent Foot(long id, double z) => new AudioEvent(id, AudioEventKind.FootContact, 0, 0, 0, z, 1.8e6, 0, 3e5, AudioMaterial.Asphalt, AudioSurface.Pavement, (uint)id);

        [Test]
        public void SilenceWithoutEvents()
        {
            var e = new SliceAudioEngine(); e.SetListener(new Listener(0, 0, 0, 0));
            var l = new float[512]; var r = new float[512]; e.Render(l, r, 512);
            foreach (var v in l) Assert.AreEqual(0f, v);
        }

        [Test]
        public void FootfallIsAudibleAndDelayedByDistance()
        {
            var e = new SliceAudioEngine(); e.SetListener(new Listener(0, 0, 0, 0));
            e.Submit(Foot(1, 686));            // 2 s at 343 m/s
            var l = new float[512]; var r = new float[512]; double before = 0, after = 0;
            for (int pos = 0; pos < 48000 * 3; pos += 512)
            {
                e.Render(l, r, 512);
                double s = 0; for (int i = 0; i < 512; i++) s += Math.Abs(l[i]) + Math.Abs(r[i]);
                if (pos + 512 <= 48000 * 2 - 600) before += s; else after += s;
            }
            Assert.AreEqual(0, before); Assert.Greater(after, 1.0);
        }

        [Test]
        public void QuakeArrivesAtSeismicSpeedNotSoundSpeed()
        {
            var e = new SliceAudioEngine(); e.SetListener(new Listener(0, 0, 0, 0));
            e.Submit(new AudioEvent(1, AudioEventKind.QuakeP, 0, 6000, 0, 0, 1e13, 0, 0, AudioMaterial.Rock, AudioSurface.Ground, 1));   // 6 km: 1 s P, 17 s as sound
            var l = new float[512]; var r = new float[512]; double early = 0;
            for (int pos = 0; pos < 48000 * 3; pos += 512) { e.Render(l, r, 512); if (pos > 48000 * 1.2) for (int i = 0; i < 512; i++) early += Math.Abs(l[i]) + Math.Abs(r[i]); }
            Assert.Greater(early, 1.0);
        }

        [Test]
        public void PanFollowsSide()
        {
            var e = new SliceAudioEngine(); e.SetListener(new Listener(0, 0, 0, 0));
            e.Submit(new AudioEvent(1, AudioEventKind.FootContact, 0, 100, 0, 0, 1.8e6, 0, 3e5, AudioMaterial.Soil, AudioSurface.Ground, 1));   // right of a +z-facing listener
            var l = new float[512]; var r = new float[512]; double sl = 0, sr = 0;
            for (int pos = 0; pos < 48000; pos += 512) { e.Render(l, r, 512); for (int i = 0; i < 512; i++) { sl += Math.Abs(l[i]); sr += Math.Abs(r[i]); } }
            Assert.Greater(sr, sl * 1.5);
        }

        [Test]
        public void VoiceCapAndLimiterHoldUnderBurst()
        {
            var e = new SliceAudioEngine(); e.SetListener(new Listener(0, 0, 0, 0));
            var burst = new List<AudioEvent>();
            for (int i = 0; i < 200; i++) burst.Add(new AudioEvent(i + 1, i % 2 == 0 ? AudioEventKind.StructureFailure : AudioEventKind.Impact, 0, i, 5, 40, 5e10, 0, 1e6, AudioMaterial.Concrete, AudioSurface.Building, (uint)i));
            e.Submit(burst);
            var l = new float[512]; var r = new float[512];
            for (int pos = 0; pos < 48000 * 2; pos += 512)
            {
                e.Render(l, r, 512);
                Assert.LessOrEqual(e.ActiveVoices, e.Profile.MaxVoices);
                for (int i = 0; i < 512; i++) { Assert.LessOrEqual(Math.Abs(l[i]), (float)e.Profile.LimiterCeiling + 1e-6f); Assert.LessOrEqual(Math.Abs(r[i]), (float)e.Profile.LimiterCeiling + 1e-6f); }
            }
            var st = e.Stats;
            Assert.LessOrEqual(st.PeakActive, e.Profile.MaxVoices);
            Assert.Greater(st.RejectedByPlanner, 0);
        }

        [Test]
        public void VoicesReleaseWhenFinished()
        {
            var e = new SliceAudioEngine(); e.SetListener(new Listener(0, 0, 0, 0));
            e.Submit(Foot(1, 20)); Assert.AreEqual(1, e.ActiveVoices);
            var l = new float[512]; var r = new float[512];
            for (int pos = 0; pos < 48000 * 6; pos += 512) e.Render(l, r, 512);
            Assert.AreEqual(0, e.ActiveVoices);
        }

        [Test]
        public void InaudibleEventsAreCulled()
        {
            var e = new SliceAudioEngine(); e.SetListener(new Listener(0, 0, 0, 0));
            e.Submit(new AudioEvent(1, AudioEventKind.FootContact, 0, 0, 0, 5e6, 10, 0, 1, AudioMaterial.Soil, AudioSurface.Ground, 1));
            Assert.AreEqual(0, e.ActiveVoices); Assert.AreEqual(1, e.Stats.RejectedByPlanner);
        }
    }

    [TestFixture]
    public class CaptureTests
    {
        static SliceCaptureResult Full()
        {
            var r = SliceAudioCapture.Run(SliceAudioCapture.FullScript(), 28.0);
            SliceAudioCapture.SoloMetrics(r);
            return r;
        }

        [Test]
        public void NoClippingAboveLimiterCeiling_AndWavSamplesInRange()
        {
            var r = Full();
            Assert.LessOrEqual(r.PeakOut, r.LimiterCeiling + 1e-6);
            // decode wav: |sample| <= ceiling * 32767 (+1 rounding)
            int max = 0;
            for (int i = 44; i + 1 < r.Wav.Length; i += 2) max = Math.Max(max, Math.Abs((int)BitConverter.ToInt16(r.Wav, i)));
            Assert.LessOrEqual(max, r.LimiterCeiling * 32767.0 + 1.0);
            Assert.Greater(max, 1000);
        }

        [Test]
        public void VoiceCapRespected_AndBurstWasActuallyOverCapacity()
        {
            var r = Full();
            Assert.LessOrEqual(r.PeakActive, r.MaxVoices);
            Assert.Greater(r.Submitted, r.MaxVoices * 2);
            Assert.Greater(r.RejectedByPlanner + r.Stolen, 0);
        }

        [Test]
        public void EveryEventClassIsNonSilent()
        {
            var r = Full();
            Assert.GreaterOrEqual(r.SoloRms.Count, 9);
            foreach (var kv in r.SoloRms) Assert.Greater(kv.Value, 1e-4, kv.Key + " rms");
        }

        [Test]
        public void SameBytesAcrossTwoRuns()
        {
            var a = SliceAudioCapture.Run(SliceAudioCapture.FullScript(), 28.0);
            var b = SliceAudioCapture.Run(SliceAudioCapture.FullScript(), 28.0);
            Assert.AreEqual(a.Wav.Length, b.Wav.Length);
            CollectionAssert.AreEqual(a.Wav, b.Wav);
            Assert.AreEqual(a.Sha256Hex, b.Sha256Hex);
        }

        [Test]
        public void ScriptCoversEveryEventClassWithAStart()
        {
            var r = SliceAudioCapture.Run(SliceAudioCapture.FullScript(), 28.0);
            foreach (var k in new[] { AudioEventKind.FootContact, AudioEventKind.Impact, AudioEventKind.StructureFailure, AudioEventKind.DebrisRain, AudioEventKind.QuakeP, AudioEventKind.QuakeS, AudioEventKind.QuakeRumble, AudioEventKind.MuzzleBlast })
                Assert.IsTrue(r.StartedByKind.ContainsKey(k), k.ToString());
        }

        [Test]
        public void WritesEvidenceWhenAsked()
        {
            string dir = Environment.GetEnvironmentVariable("KAIJU_AUDIO_EVIDENCE_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("KAIJU_AUDIO_EVIDENCE_DIR not set");
            var r = Full();
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "slice_audio.wav"), r.Wav);
            File.WriteAllText(Path.Combine(dir, "slice_audio_metrics.json"), r.ToJson());
        }
    }
}
