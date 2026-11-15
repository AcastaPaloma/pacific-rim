using System;
using NUnit.Framework;
using Game.Audio;

namespace Game.Audio.Tests
{
    public class AudioMathTests
    {
        static readonly AudioProfile P = new AudioProfile { TemperatureC = 20, RelativeHumidityPct = 70 };

        [Test] public void SpeedOfSound_20C_Is343() => Assert.AreEqual(343.2, AudioMath.SpeedOfSound(20.0), 0.3);
        [Test] public void SpeedOfSound_RisesWithTemperature() => Assert.Greater(AudioMath.SpeedOfSound(30), AudioMath.SpeedOfSound(0));

        [Test] public void Delay_At1km_IsAbout2p9s()
        {
            double d = AudioMath.PropagationDelayS(1000.0, AudioMath.SpeedOfSound(15.0));
            Assert.AreEqual(2.94, d, 0.03);
            Assert.That(d, Is.InRange(2.8, 3.0));
        }
        [Test] public void Delay_IsLinearInDistance() => Assert.AreEqual(2 * AudioMath.PropagationDelayS(500, 340), AudioMath.PropagationDelayS(1000, 340), 1e-12);

        [Test] public void DistanceAttenuation_6dBPerDoubling()
            => Assert.AreEqual(6.0206, AudioMath.DistanceAttenuationDb(20) - AudioMath.DistanceAttenuationDb(10), 1e-3);
        [Test] public void DistanceAttenuation_ZeroAtOrInside1m() { Assert.AreEqual(0.0, AudioMath.DistanceAttenuationDb(1.0)); Assert.AreEqual(0.0, AudioMath.DistanceAttenuationDb(0.2)); }
        [Test] public void DistanceAttenuation_100m_Is40dB() => Assert.AreEqual(40.0, AudioMath.DistanceAttenuationDb(100.0), 1e-9);

        // ISO 9613-1 spot values (formula of the standard; table: 20 C 70 percent about 5.0 dB/km at 1 kHz; 10 C 70 percent 3.66 / 33.1 / 118 dB/km at 1 / 4 / 8 kHz).
        [Test] public void Iso9613_1kHz_20C_70pct() => Assert.AreEqual(4.98e-3, AudioMath.AtmosphericAbsorptionDbPerM(1000, 20, 70, 101.325), 0.1e-3);
        [Test] public void Iso9613_1kHz_10C_70pct() => Assert.AreEqual(3.66e-3, AudioMath.AtmosphericAbsorptionDbPerM(1000, 10, 70, 101.325), 0.1e-3);
        [Test] public void Iso9613_4kHz_10C_70pct() => Assert.AreEqual(33.06e-3, AudioMath.AtmosphericAbsorptionDbPerM(4000, 10, 70, 101.325), 0.5e-3);
        [Test] public void Iso9613_8kHz_10C_70pct() => Assert.AreEqual(118.4e-3, AudioMath.AtmosphericAbsorptionDbPerM(8000, 10, 70, 101.325), 1.5e-3);
        [Test] public void Iso9613_Infrasound_IsNegligible() => Assert.Less(AudioMath.AtmosphericAbsorptionDbPerM(16, 15, 75, 101.325) * 1000.0, 0.05);
        [Test] public void Iso9613_MonotoneInFrequency()
        {
            double prev = 0;
            foreach (double f in AudioMath.BandCentresHz) { double a = AudioMath.AtmosphericAbsorptionDbPerM(f, 15, 75, 101.325); Assert.Greater(a, prev); prev = a; }
        }
        [Test] public void Lowpass_FallsWithDistance()
        {
            double a = AudioMath.AtmosphericLowpassHz(200, P), b = AudioMath.AtmosphericLowpassHz(2000, P);
            Assert.Greater(a, b);
        }

        [Test] public void Spl_FootfallOfMech_IsLoudAndClamped()
        {
            AudioMath.KindAcoustics(AudioEventKind.FootContact, out double eta, out double dur);
            double spl = AudioMath.SplAt1m(3e7, eta, dur);
            Assert.That(spl, Is.InRange(150.0, 194.0));
        }
        [Test] public void Spl_MonotoneInEnergy() => Assert.Greater(AudioMath.SplAt1m(2e6, 1e-3, 0.1), AudioMath.SplAt1m(1e6, 1e-3, 0.1));
        [Test] public void Spl_TenTimesEnergy_Is10dB() => Assert.AreEqual(10.0, AudioMath.SplAt1m(1e7, 1e-3, 0.1, 400) - AudioMath.SplAt1m(1e6, 1e-3, 0.1, 400), 1e-9);
        [Test] public void Spl_ZeroEnergy_IsZero() => Assert.AreEqual(0.0, AudioMath.SplAt1m(0.0, 1e-3, 0.1));
        [Test] public void Spl_ClampedAtMax() => Assert.AreEqual(194.0, AudioMath.SplAt1m(1e15, 0.1, 0.01), 1e-9);

        [Test] public void Doppler_ApproachingSource_RaisesPitch()
        {
            var ev = new AudioEvent(1, AudioEventKind.ProjectileWhistle, 0, 0, 0, 0, 1, 0, 1, AudioMaterial.Steel, AudioSurface.Ground, 0, 0, 0, 170);
            var l = new Listener(0, 0, 1000, 0);
            Assert.AreEqual(2.0, AudioMath.DopplerRatio(ev, l, 340.0), 1e-9);
        }
        [Test] public void Doppler_Static_IsUnity()
        {
            var ev = new AudioEvent(1, AudioEventKind.Impact, 0, 5, 0, 5, 1, 0, 1, AudioMaterial.Steel, AudioSurface.Ground);
            Assert.AreEqual(1.0, AudioMath.DopplerRatio(ev, new Listener(0, 0, 0, 0), 343), 1e-12);
        }
        [Test] public void Doppler_Receding_LowersPitch()
        {
            var ev = new AudioEvent(1, AudioEventKind.ProjectileWhistle, 0, 0, 0, 0, 1, 0, 1, AudioMaterial.Steel, AudioSurface.Ground, 0, 0, 0, -100);
            Assert.Less(AudioMath.DopplerRatio(ev, new Listener(0, 0, 1000, 0), 343), 1.0);
        }
        [Test] public void Doppler_SupersonicApproach_IsBounded()
        {
            var ev = new AudioEvent(1, AudioEventKind.ProjectileWhistle, 0, 0, 0, 0, 1, 0, 1, AudioMaterial.Steel, AudioSurface.Ground, 0, 0, 0, 900);
            Assert.LessOrEqual(AudioMath.DopplerRatio(ev, new Listener(0, 0, 1000, 0), 343), 4.0);
        }

        [Test] public void Occlusion_IncreasesAndCaps()
        {
            Assert.AreEqual(0, AudioMath.OcclusionDb(0, P));
            Assert.Greater(AudioMath.OcclusionDb(2, P), AudioMath.OcclusionDb(1, P));
            Assert.AreEqual(P.OcclusionMaxDb, AudioMath.OcclusionDb(50, P));
            Assert.Less(AudioMath.OcclusionLowpassFactor(2), AudioMath.OcclusionLowpassFactor(1));
        }

        [Test] public void Pan_SourceToTheRight_IsPositive()
        {
            var ev = new AudioEvent(1, AudioEventKind.Impact, 0, 100, 0, 0, 1, 0, 1, AudioMaterial.Steel, AudioSurface.Ground);
            AudioMath.PanAndItd(ev, new Listener(0, 0, 0, 0), 343, out double pan, out double itd);
            Assert.AreEqual(1.0, pan, 1e-9); Assert.Greater(itd, 0.0005); Assert.Less(itd, 0.0008);
        }
        [Test] public void PanGains_ConstantPower()
        {
            for (double p = -1; p <= 1; p += 0.25) { AudioMath.PanGains(p, out double l, out double r); Assert.AreEqual(1.0, l * l + r * r, 1e-12); }
        }

        [Test] public void SplToDbfs_MapsEndpointsAndMonotone()
        {
            Assert.AreEqual(-3.0, AudioMath.SplToDbfs(140, P), 1e-9);
            Assert.AreEqual(-3.0, AudioMath.SplToDbfs(190, P), 1e-9);
            Assert.IsTrue(double.IsNegativeInfinity(AudioMath.SplToDbfs(30, P)));
            Assert.Greater(AudioMath.SplToDbfs(100, P), AudioMath.SplToDbfs(60, P));
            Assert.AreEqual(0.0, AudioMath.DbfsToLinear(double.NegativeInfinity));
        }

        [Test] public void Seismic_CornerFrequencyFallsWithMw()
        {
            Assert.Less(AudioMath.QuakeCornerFrequencyHz(7.2), AudioMath.QuakeCornerFrequencyHz(5.0));
            Assert.Less(AudioMath.QuakeCornerFrequencyHz(7.2), 0.2); // infrasonic
        }
        [Test] public void Seismic_PArrivesBeforeS() => Assert.Less(AudioMath.QuakeArrivalS(30000, false), AudioMath.QuakeArrivalS(30000, true));
        [Test] public void Seismic_MomentMw7_IsAbout4e19() => Assert.AreEqual(3.98e19, AudioMath.SeismicMomentNm(7.0), 0.05e19);
        [Test] public void Seismic_RumbleDb_MonotoneAndClamped()
        {
            Assert.Greater(AudioMath.QuakeRumbleDb(0.3), AudioMath.QuakeRumbleDb(0.03));
            Assert.AreEqual(120.0, AudioMath.QuakeRumbleDb(100.0));
        }
    }

    public class ModalTests
    {
        static double Centroid(AudioMaterial m, double kg) { var a = new Mode[ModalSynth.MaxModes]; int n = ModalSynth.Build(m, kg, 7, a); return ModalSynth.SpectralCentroidHz(a, n); }

        [Test] public void Centroid_FallsWithMass()
        {
            Assert.Greater(Centroid(AudioMaterial.Steel, 1), Centroid(AudioMaterial.Steel, 1000));
            Assert.Greater(Centroid(AudioMaterial.Steel, 1000), Centroid(AudioMaterial.Steel, 6e6));
        }
        [Test] public void Centroid_MechFoot_IsLowFrequency() => Assert.Less(Centroid(AudioMaterial.Concrete, 6e6), 50.0);
        [Test] public void FundamentalScales_AsInverseCubeRoot()
        {
            var a = new Mode[8]; var b = new Mode[8];
            ModalSynth.Build(AudioMaterial.Steel, 1000, 0, a); ModalSynth.Build(AudioMaterial.Steel, 8000, 0, b);
            Assert.AreEqual(0.5, b[0].FreqHz / a[0].FreqHz, 0.03);
        }
        [Test] public void Steel_RingsLongerThanConcrete_AndGlassBrighter()
        {
            var s = new Mode[8]; var c = new Mode[8]; var g = new Mode[8];
            ModalSynth.Build(AudioMaterial.Steel, 100, 1, s); ModalSynth.Build(AudioMaterial.Concrete, 100, 1, c); ModalSynth.Build(AudioMaterial.Glass, 100, 1, g);
            Assert.Greater(s[0].T60S, c[0].T60S);
            Assert.Greater(g[0].FreqHz, c[0].FreqHz);
        }
        [Test] public void Build_IsDeterministic_AndSeedChangesDetune()
        {
            var a = new Mode[8]; var b = new Mode[8]; var c = new Mode[8];
            ModalSynth.Build(AudioMaterial.Glass, 5, 42, a); ModalSynth.Build(AudioMaterial.Glass, 5, 42, b); ModalSynth.Build(AudioMaterial.Glass, 5, 43, c);
            for (int i = 0; i < 8; i++) Assert.AreEqual(a[i].FreqHz, b[i].FreqHz);
            Assert.AreNotEqual(a[0].FreqHz, c[0].FreqHz);
            Assert.AreEqual(a[0].FreqHz, c[0].FreqHz, a[0].FreqHz * 0.03); // detune within 2 percent
        }
        [Test] public void Render_IsDeterministic_AndPeakBounded()
        {
            var m = new Mode[8]; ModalSynth.Build(AudioMaterial.Steel, 50, 3, m);
            var x = new float[4096]; var y = new float[4096];
            ModalSynth.Render(m, 8, 48000, x, 4096, 0.5f); ModalSynth.Render(m, 8, 48000, y, 4096, 0.5f);
            float mx = 0; for (int i = 0; i < x.Length; i++) { Assert.AreEqual(x[i], y[i]); mx = Math.Max(mx, Math.Abs(x[i])); }
            Assert.AreEqual(0.5f, mx, 1e-4f);
        }
        [Test] public void Hash_InRange() { for (int k = 0; k < 100; k++) Assert.That(ModalSynth.Hash01(99, k), Is.InRange(-1.0, 1.0)); }
    }

    public class VoiceTests
    {
        [Test] public void FillsToBudget_ThenRejectsEqualScore()
        {
            var a = new VoiceAllocator(4, 0, 3.0);
            for (int i = 0; i < 4; i++) Assert.IsTrue(a.Request(i, AudioEventKind.Impact, 20, i).Accepted);
            Assert.AreEqual(4, a.ActiveCount);
            Assert.IsFalse(a.Request(9, AudioEventKind.Impact, 20, 9).Accepted);
        }
        [Test] public void LouderEvent_StealsQuietest()
        {
            var a = new VoiceAllocator(2, 0, 3.0);
            a.Request(1, AudioEventKind.Impact, 30, 1); a.Request(2, AudioEventKind.Impact, 10, 2);
            var g = a.Request(3, AudioEventKind.Impact, 40, 3);
            Assert.IsTrue(g.Accepted); Assert.AreEqual(2, g.StolenEventId); Assert.AreEqual(2, a.ActiveCount);
        }
        [Test] public void Hysteresis_BlocksMarginalSteal()
        {
            var a = new VoiceAllocator(1, 0, 3.0);
            a.Request(1, AudioEventKind.Impact, 20, 1);
            Assert.IsFalse(a.Request(2, AudioEventKind.Impact, 22, 2).Accepted);
            Assert.IsTrue(a.Request(3, AudioEventKind.Impact, 24, 3).Accepted);
        }
        [Test] public void ReservedVoices_OnlyForCritical()
        {
            var a = new VoiceAllocator(4, 2, 3.0);
            Assert.IsTrue(a.Request(1, AudioEventKind.DebrisRain, 10, 1).Accepted);
            Assert.IsTrue(a.Request(2, AudioEventKind.DebrisRain, 10, 2).Accepted);
            Assert.IsFalse(a.Request(3, AudioEventKind.DebrisRain, 10, 3).Accepted);
            Assert.IsTrue(a.Request(4, AudioEventKind.MuzzleBlast, 10, 4).Accepted);
            Assert.IsTrue(a.Request(5, AudioEventKind.QuakeS, 10, 5).Accepted);
            Assert.AreEqual(4, a.ActiveCount);
        }
        [Test] public void NonCritical_CannotStealCritical()
        {
            var a = new VoiceAllocator(1, 0, 3.0);
            a.Request(1, AudioEventKind.MuzzleBlast, 5, 1);
            Assert.IsFalse(a.Request(2, AudioEventKind.DebrisRain, 80, 2).Accepted);
        }
        [Test] public void Release_FreesSlot()
        {
            var a = new VoiceAllocator(1, 0, 3.0);
            var g = a.Request(1, AudioEventKind.Impact, 10, 1);
            a.Release(g.VoiceId); Assert.AreEqual(0, a.ActiveCount);
            Assert.IsTrue(a.Request(2, AudioEventKind.Impact, 1, 2).Accepted);
        }
        [Test] public void Culled_BelowFloor()
        {
            Assert.IsTrue(double.IsNegativeInfinity(VoiceAllocator.Score(20, 25, AudioEventKind.Impact)));
            Assert.IsFalse(new VoiceAllocator(2, 0, 3).Request(1, AudioEventKind.Impact, double.NegativeInfinity, 0).Accepted);
        }
        [Test] public void NeverExceedsBudget_UnderStorm()
        {
            var a = new VoiceAllocator(8, 2, 3.0);
            for (int i = 0; i < 500; i++)
            {
                a.Request(i, (AudioEventKind)(i % 13), (i * 37) % 60, i);
                Assert.LessOrEqual(a.ActiveCount, 8);
            }
        }
        [Test] public void Allocation_IsDeterministic()
        {
            long[] Run() { var a = new VoiceAllocator(4, 1, 3.0); var r = new long[200]; for (int i = 0; i < 200; i++) { var g = a.Request(i, (AudioEventKind)(i % 13), (i * 53) % 47, i); r[i] = g.Accepted ? g.VoiceId * 1000 + g.StolenEventId : -1; } return r; }
            CollectionAssert.AreEqual(Run(), Run());
        }
        [Test] public void BigFootfall_OutscoresDistantDebris()
        {
            Assert.Greater(VoiceAllocator.Score(80, 25, AudioEventKind.FootContact), VoiceAllocator.Score(80, 25, AudioEventKind.DebrisRain));
        }
    }

    public class LimiterAndPlannerTests
    {
        [Test] public void Limiter_NeverExceedsCeiling()
        {
            var lim = new Limiter(0.89, 0.25, 48000);
            for (int i = 0; i < 20000; i++)
            {
                float x = (float)(50.0 * Math.Sin(i * 0.01) * (i % 3000 < 100 ? 20 : 1));
                Assert.LessOrEqual(Math.Abs(lim.Process(x)), 0.89f + 1e-6f);
            }
        }
        [Test] public void Limiter_TransparentBelowCeiling()
        {
            var lim = new Limiter(0.89, 0.25, 48000);
            for (int i = 0; i < 1000; i++) { float x = 0.5f * (float)Math.Sin(i * 0.05); Assert.AreEqual(x, lim.Process(x), 1e-7f); }
        }
        [Test] public void Limiter_ReleasesTowardUnity()
        {
            var lim = new Limiter(0.5, 0.01, 48000);
            lim.Process(10f); float g0 = lim.CurrentGain;
            for (int i = 0; i < 4800; i++) lim.Process(0f);
            Assert.Greater(lim.CurrentGain, g0); Assert.Greater(lim.CurrentGain, 0.99f);
        }
        [Test] public void Limiter_IsDeterministic()
        {
            float[] Run() { var l = new Limiter(0.9, 0.1, 48000); var o = new float[2000]; for (int i = 0; i < 2000; i++) o[i] = l.Process((float)(3 * Math.Sin(i * 0.07))); return o; }
            CollectionAssert.AreEqual(Run(), Run());
        }
        [Test] public void Limiter_Stereo_KeepsBalance()
        {
            var lim = new Limiter(0.5, 0.1, 48000);
            var l = new float[] { 2f, 1f }; var r = new float[] { 1f, 0.5f };
            lim.ProcessStereo(l, r, 2);
            Assert.AreEqual(2.0f, l[0] / r[0], 1e-4f); Assert.LessOrEqual(Math.Abs(l[0]), 0.5f + 1e-6f);
        }

        [Test] public void Plan_1kmFootfall_DelayedAndQuieterThanNear()
        {
            var p = new AudioProfile { TemperatureC = 15 };
            var ev = new AudioEvent(1, AudioEventKind.FootContact, 100, 1000, 0, 0, 3e7, 5e6, 6e6, AudioMaterial.Concrete, AudioSurface.Pavement, 5);
            var far = AudioPlanner.Plan(ev, new Listener(0, 0, 0, 0), p);
            var near = AudioPlanner.Plan(ev, new Listener(900, 0, 0, 0), p);
            Assert.AreEqual(2.94, far.DelayS, 0.03);
            Assert.Greater(near.SplAtListenerDb, far.SplAtListenerDb);
            Assert.Greater(near.LowpassHz, far.LowpassHz);
        }
        [Test] public void Plan_Occlusion_Reduces()
        {
            var p = new AudioProfile();
            var ev = new AudioEvent(1, AudioEventKind.Impact, 0, 300, 0, 0, 1e6, 1e5, 1e4, AudioMaterial.Steel, AudioSurface.Ground);
            var l = new Listener(0, 0, 0, 0);
            Assert.Less(AudioPlanner.Plan(ev, l, p, 2).SplAtListenerDb, AudioPlanner.Plan(ev, l, p, 0).SplAtListenerDb);
        }
        [Test] public void Plan_IsDeterministic()
        {
            var p = new AudioProfile();
            var ev = new AudioEvent(1, AudioEventKind.StructureFailure, 5, 120, 10, -40, 5e8, 1e6, 2e6, AudioMaterial.Glass, AudioSurface.Building, 11);
            var l = new Listener(3, 1, 4, 0.3);
            var a = AudioPlanner.Plan(ev, l, p, 1); var b = AudioPlanner.Plan(ev, l, p, 1);
            Assert.AreEqual(a.SplAtListenerDb, b.SplAtListenerDb); Assert.AreEqual(a.Score, b.Score); Assert.AreEqual(a.Pan, b.Pan);
        }
        [Test] public void TryStart_CullsInaudible()
        {
            var p = new AudioProfile(); var alloc = new VoiceAllocator(p.MaxVoices, p.ReservedCriticalVoices, p.StealHysteresisDb);
            var ev = new AudioEvent(1, AudioEventKind.Wade, 0, 50000, 0, 0, 10, 1, 100, AudioMaterial.Water, AudioSurface.Water);
            Assert.IsFalse(AudioPlanner.TryStart(ev, new Listener(0, 0, 0, 0), p, alloc, 0, out _, out _));
        }
        [Test] public void TryStart_AcceptsLoudNear()
        {
            var p = new AudioProfile(); var alloc = new VoiceAllocator(p.MaxVoices, p.ReservedCriticalVoices, p.StealHysteresisDb);
            var ev = new AudioEvent(7, AudioEventKind.MuzzleBlast, 3, 20, 0, 0, 1e8, 1e5, 1000, AudioMaterial.Steel, AudioSurface.Ground);
            Assert.IsTrue(AudioPlanner.TryStart(ev, new Listener(0, 0, 0, 0), p, alloc, 0, out var cmd, out _));
            Assert.AreEqual(7, cmd.Event.Id); Assert.Greater(cmd.StartDelayS, 0.0);
        }
        [Test] public void Flag_NameIsStable() => Assert.AreEqual("audio.physical", AudioFlags.Physical);
    }
}
