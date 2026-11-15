using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.Net.Tests
{
    /// <summary>StateSnapshot@v1 codec and client interpolation (T-029 step 2, M-056 style, in world metres).</summary>
    public class NetSnapshotTests
    {
        private const double StepSeconds = 0.02;
        private const int SnapshotEvery = 2; // 25 Hz

        // ---------- codec ----------

        private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

        private static void AssertBitEqual(StateSnapshot a, StateSnapshot b)
        {
            Assert.AreEqual(a.HostStep, b.HostStep);
            Assert.AreEqual(a.Entities.Length, b.Entities.Length);
            for (int i = 0; i < a.Entities.Length; i++)
            {
                var x = a.Entities[i]; var y = b.Entities[i];
                Assert.AreEqual(x.Id, y.Id);
                CollectionAssert.AreEqual(
                    new[] { Bits(x.PosX), Bits(x.PosY), Bits(x.PosZ), Bits(x.VelX), Bits(x.VelY), Bits(x.VelZ), Bits(x.Yaw), Bits(x.YawRate) },
                    new[] { Bits(y.PosX), Bits(y.PosY), Bits(y.PosZ), Bits(y.VelX), Bits(y.VelY), Bits(y.VelZ), Bits(y.Yaw), Bits(y.YawRate) });
            }
        }

        private static StateSnapshot Weird() => new StateSnapshot
        {
            HostStep = long.MaxValue - 3,
            Entities = new[]
            {
                new EntityState(EntityId.FromValue(1), -0f, float.Epsilon, float.MaxValue, float.NegativeInfinity, float.PositiveInfinity,
                    BitConverter.Int32BitsToSingle(0x7FC12345), 0.1f, -3.14159f),
                new EntityState(EntityId.FromValue(ulong.MaxValue), 1, 2, 3, 4, 5, 6, 7, 8),
            },
        };

        [Test]
        public void Codec_RoundTrip_IsBitExact_IncludingNanPayloadAndNegativeZero()
        {
            var s = Weird();
            var bytes = SnapshotCodec.Encode(s);
            Assert.IsTrue(SnapshotCodec.TryDecode(bytes, out var d));
            AssertBitEqual(s, d);
            Assert.AreEqual(SnapshotCodec.HeaderBytes + 2 * SnapshotCodec.EntityBytes, bytes.Length);
            Assert.AreEqual(bytes.Length, SnapshotCodec.EncodedSize(s));
            Assert.AreEqual(NetContractVersions.StateSnapshot, bytes[0]);
        }

        [Test]
        public void Codec_RandomSnapshots_RoundTripAndEncodeIdentically()
        {
            var rng = new Random(1234);
            for (int round = 0; round < 200; round++)
            {
                int n = rng.Next(0, 40);
                var list = new List<EntityState>();
                ulong id = 0;
                for (int i = 0; i < n; i++)
                {
                    id += (ulong)rng.Next(1, 1000);
                    float F() => BitConverter.Int32BitsToSingle(rng.Next(int.MinValue, int.MaxValue));
                    list.Add(new EntityState(EntityId.FromValue(id), F(), F(), F(), F(), F(), F(), F(), F()));
                }
                var s = new StateSnapshot { HostStep = rng.NextInt64(), Entities = list.ToArray() };
                var b1 = SnapshotCodec.Encode(s);
                Assert.IsTrue(SnapshotCodec.TryDecode(b1, out var d));
                AssertBitEqual(s, d);
                CollectionAssert.AreEqual(b1, SnapshotCodec.Encode(d));
            }
        }

        [Test]
        public void Codec_EmptySnapshot_RoundTrips()
        {
            var s = new StateSnapshot { HostStep = 0 };
            Assert.IsTrue(SnapshotCodec.TryDecode(SnapshotCodec.Encode(s), out var d));
            Assert.AreEqual(0, d.Entities.Length);
        }

        [Test]
        public void Codec_RejectsMalformedInput()
        {
            var good = SnapshotCodec.Encode(Weird());
            for (int len = 0; len < good.Length; len++)
                Assert.IsFalse(SnapshotCodec.TryDecode(good.AsSpan(0, len).ToArray(), out _), "prefix " + len);
            var trailing = new byte[good.Length + 1]; Array.Copy(good, trailing, good.Length);
            Assert.IsFalse(SnapshotCodec.TryDecode(trailing, out _));
            var wrongVersion = (byte[])good.Clone(); wrongVersion[0] = 2;
            Assert.IsFalse(SnapshotCodec.TryDecode(wrongVersion, out _));
            var hugeCount = (byte[])good.Clone(); hugeCount[9] = 0xFF; hugeCount[10] = 0xFF; hugeCount[11] = 0xFF; hugeCount[12] = 0x7F;
            Assert.IsFalse(SnapshotCodec.TryDecode(hugeCount, out _));
            Assert.IsFalse(SnapshotCodec.TryDecode(null, out _));

            var dup = new StateSnapshot { HostStep = 1, Entities = new[] { new EntityState(EntityId.FromValue(5), 0, 0, 0, 0, 0, 0, 0, 0), new EntityState(EntityId.FromValue(5), 0, 0, 0, 0, 0, 0, 0, 0) } };
            Assert.IsFalse(SnapshotCodec.TryDecode(SnapshotCodec.Encode(dup), out _));
            var unsorted = new StateSnapshot { HostStep = 1, Entities = new[] { new EntityState(EntityId.FromValue(6), 0, 0, 0, 0, 0, 0, 0, 0), new EntityState(EntityId.FromValue(5), 0, 0, 0, 0, 0, 0, 0, 0) } };
            Assert.IsFalse(SnapshotCodec.TryDecode(SnapshotCodec.Encode(unsorted), out _));
        }

        // ---------- scripted host paths ----------

        // Body 1: circle r = 3 m at 0.5 rad/s (v = 1.5 m/s, a = 0.75 m/s^2) with a slow vertical wobble.
        // Body 2: Lissajous (2 cos 0.4t, 0, sin 0.8t): v <= 0.8 m/s per axis, a <= 0.64 m/s^2.
        private static readonly EntityId Id1 = EntityId.FromValue(10), Id2 = EntityId.FromValue(20);

        private static void Truth(int body, double t, out double x, out double y, out double z, out double vx, out double vy, out double vz, out double yaw, out double yawRate)
        {
            if (body == 0)
            {
                x = 3 * Math.Sin(0.5 * t); z = 3 * Math.Cos(0.5 * t); y = 0.2 * Math.Sin(0.3 * t);
                vx = 1.5 * Math.Cos(0.5 * t); vz = -1.5 * Math.Sin(0.5 * t); vy = 0.06 * Math.Cos(0.3 * t);
                yaw = 0.5 * t; yawRate = 0.5;
            }
            else
            {
                x = 2 * Math.Cos(0.4 * t); z = Math.Sin(0.8 * t); y = 0;
                vx = -0.8 * Math.Sin(0.4 * t); vz = 0.8 * Math.Cos(0.8 * t); vy = 0;
                yaw = 0.4 * t; yawRate = 0.4;
            }
        }

        private static StateSnapshot HostSnapshot(long step)
        {
            double t = step * StepSeconds;
            var e = new EntityState[2];
            for (int b = 0; b < 2; b++)
            {
                Truth(b, t, out var x, out var y, out var z, out var vx, out var vy, out var vz, out var yaw, out var yr);
                e[b] = new EntityState(b == 0 ? Id1 : Id2, (float)x, (float)y, (float)z, (float)vx, (float)vy, (float)vz, (float)yaw, (float)yr);
            }
            return new StateSnapshot { HostStep = step, Entities = e };
        }

        private static double AngleDiff(double a, double b) => Math.Abs(Math.IEEERemainder(a - b, 2 * Math.PI));

        private sealed class RunResult
        {
            public double MaxErr, MaxYawErr, MaxFrameMove, MaxErrAfter;
            public bool SawStale;
            public int Dropped, Frames;
            public ulong Hash = 14695981039346656037UL;
            public double[] Err;
        }

        /// <summary>Host at 25 Hz over a SimulatedLinkTransport (one-way 2..3 steps = 50 ms +-10 ms, i.e. RTT 100 ms +-10 ms), client interpolating.</summary>
        private static RunResult Simulate(ulong seed, int steps, double loss, int outageStart = -1, int outageSteps = 0, int errAfterStep = int.MaxValue)
        {
            LoopbackTransport.CreatePair(out var hostEnd, out var clientEnd);
            var link = new SimulatedLinkTransport(hostEnd, seed, new LinkConditions { DelaySteps = 2, JitterSteps = 1, UnreliableLoss = loss });
            var interp = new SnapshotInterpolator(StepSeconds, SnapshotEvery);
            var res = new RunResult { Err = new double[steps] };
            double px = 0, py = 0, pz = 0; bool havePrev = false;
            for (int n = 0; n < steps; n++)
            {
                link.Advance(n);
                link.Conditions.UnreliableLoss = (n >= outageStart && n < outageStart + outageSteps) ? 1.0 : loss;
                if (n % SnapshotEvery == 0) link.Send(NetChannel.Unreliable, SnapshotCodec.Encode(HostSnapshot(n)));
                while (clientEnd.TryReceive(out _, out var payload))
                {
                    Assert.IsTrue(SnapshotCodec.TryDecode(payload, out var snap));
                    interp.Receive(n, snap);
                }
                interp.Update(n);
                if (interp.IsStale) res.SawStale = true;
                if (interp.Bodies.Count != 2) { havePrev = false; continue; }
                double rt = interp.RenderStep * StepSeconds, worst = 0;
                for (int b = 0; b < 2; b++)
                {
                    var body = interp.Bodies[b];
                    Truth(b, rt, out var x, out var y, out var z, out _, out _, out _, out var yaw, out _);
                    double err = Math.Sqrt((body.PosX - x) * (body.PosX - x) + (body.PosY - y) * (body.PosY - y) + (body.PosZ - z) * (body.PosZ - z));
                    worst = Math.Max(worst, err);
                    if (n >= 40) res.MaxYawErr = Math.Max(res.MaxYawErr, AngleDiff(body.Yaw, yaw));
                    unchecked
                    {
                        res.Hash = (res.Hash ^ (ulong)BitConverter.DoubleToInt64Bits(body.PosX)) * 1099511628211UL;
                        res.Hash = (res.Hash ^ (ulong)BitConverter.DoubleToInt64Bits(body.PosY)) * 1099511628211UL;
                        res.Hash = (res.Hash ^ (ulong)BitConverter.DoubleToInt64Bits(body.PosZ)) * 1099511628211UL;
                        res.Hash = (res.Hash ^ (ulong)BitConverter.DoubleToInt64Bits(body.Yaw)) * 1099511628211UL;
                    }
                    if (b == 0)
                    {
                        if (havePrev) res.MaxFrameMove = Math.Max(res.MaxFrameMove, Math.Sqrt((body.PosX - px) * (body.PosX - px) + (body.PosY - py) * (body.PosY - py) + (body.PosZ - pz) * (body.PosZ - pz)));
                        px = body.PosX; py = body.PosY; pz = body.PosZ; havePrev = true;
                    }
                }
                res.Err[n] = worst;
                res.Frames++;
                if (n >= 40) res.MaxErr = Math.Max(res.MaxErr, worst);
                if (n >= errAfterStep) res.MaxErrAfter = Math.Max(res.MaxErrAfter, worst);
            }
            res.Dropped = link.DroppedCount;
            return res;
        }

        // World-space bound for the steady cell (RTT 100 ms +-10 ms, 2 % loss, 120 s), derived:
        //  - Hermite interpolation error <= h^4/384 * max|p''''|. Worst gap h = 0.2 s (5 intervals, i.e. 4 lost in a row);
        //    max|p''''| = A w^4 <= 0.41 m/s^4 (body 2) -> <= 1.7e-6 m.
        //  - float32 on the wire: half-ulp at 3 m is 1.2e-7 m per component -> < 1e-6 m.
        //  - Extrapolation, only if the 3-interval (6 step) buffer runs dry: 1/2 a t^2 with a <= 0.75 m/s^2. The clock offset
        //    keeps the render time ~2-3 steps behind the newest arrival margin, so overrun is at most one interval (t = 40 ms):
        //    0.5 * 0.75 * 0.04^2 = 6.0e-4 m.
        //  Sum ~ 6.0e-4 m; stated bound 1 mm (1e-3 m). Pixel conversion (focal px / distance) is out of scope here.
        private const double SteadyBoundMetres = 1e-3;

        [Test]
        public void Interpolation_SlowPath_RTT100_Jitter10_Loss2pct_ErrorBelowBound()
        {
            var r = Simulate(seed: 7, steps: 6000, loss: 0.02);
            Assert.Greater(r.Dropped, 20, "loss must actually occur");
            Assert.Greater(r.Frames, 5950, "bodies present on (nearly) every frame after the first snapshots");
            Assert.Less(r.MaxErr, SteadyBoundMetres, "max world-space error");
            Assert.Less(r.MaxYawErr, 1e-3);
            Assert.IsFalse(r.SawStale, "no freeze in the steady cell");
        }

        [Test]
        public void Interpolation_400msOutage_NoJumpBeyondDampedCorrection_AndRecoversWithinOneSecond()
        {
            const int outageStart = 1000, outageLen = 20; // 400 ms
            var r = Simulate(seed: 11, steps: 2000, loss: 0.02, outageStart, outageLen, errAfterStep: outageStart + outageLen + 3 + 50);
            Assert.IsTrue(r.SawStale, "outage longer than the 250 ms extrapolation limit must freeze and flag stale");
            double vmax = 1.5;
            // Target moves <= vmax*dt per frame; the damped correction adds at most omega*J/e ~ 0.26 m/s for the ~0.06 m the
            // frozen target is behind. A teleport of that size would give ~0.09 m in one frame.
            Assert.Less(r.MaxFrameMove, 1.35 * vmax * StepSeconds, "per-frame movement");
            Assert.Less(r.MaxErrAfter, SteadyBoundMetres, "recovered within 1 s of the first post-outage snapshot");
            double peak = 0; for (int n = outageStart; n < outageStart + 80; n++) peak = Math.Max(peak, r.Err[n]);
            Assert.Greater(peak, 5e-3, "scenario must actually exercise the correction");
        }

        [Test]
        public void Interpolation_SameSeed_IdenticalOutput_DifferentSeed_Differs()
        {
            var a = Simulate(5, 1500, 0.02, 600, 20);
            var b = Simulate(5, 1500, 0.02, 600, 20);
            var c = Simulate(6, 1500, 0.02, 600, 20);
            Assert.AreEqual(a.Hash, b.Hash);
            Assert.AreEqual(a.Dropped, b.Dropped);
            Assert.AreNotEqual(a.Hash, c.Hash);
        }

        // ---------- interpolator behaviour ----------

        private static StateSnapshot Line(long step, double speed)
        {
            double t = step * StepSeconds;
            return new StateSnapshot { HostStep = step, Entities = new[] { new EntityState(Id1, (float)(speed * t), 0, 0, (float)speed, 0, 0, 0, 0) } };
        }

        [Test]
        public void Hermite_ReproducesCubicPathExactly_BetweenSnapshots()
        {
            var interp = new SnapshotInterpolator(StepSeconds, SnapshotEvery);
            double X(double t) => 0.5 * t * t * t - t * t + t;
            double V(double t) => 1.5 * t * t - 2 * t + 1;
            double worst = 0;
            for (int n = 0; n < 200; n++)
            {
                if (n % 2 == 0)
                {
                    double t = n * StepSeconds;
                    interp.Receive(n, new StateSnapshot { HostStep = n, Entities = new[] { new EntityState(Id1, (float)X(t), 0, 0, (float)V(t), 0, 0, 0, 0) } });
                }
                interp.Update(n);
                if (n > 30) worst = Math.Max(worst, Math.Abs(interp.Bodies[0].PosX - X(interp.RenderStep * StepSeconds)));
            }
            Assert.Less(worst, 1e-5);
        }

        [Test]
        public void Extrapolation_IsBoundedTo250ms_ThenFreezesAndFlagsStale()
        {
            var interp = new SnapshotInterpolator(StepSeconds, SnapshotEvery);
            interp.Receive(0, Line(0, 1.0));
            interp.Receive(2, Line(2, 1.0));   // clock offset 0, so render step = client step - 6
            interp.Update(13);                 // render step 7: 5 steps past the newest snapshot
            Assert.IsFalse(interp.IsStale);
            Assert.AreEqual(1.0 * 7 * StepSeconds, interp.Bodies[0].PosX, 1e-5);
            interp.Update(28);                 // render step 22: 20 steps (400 ms) past
            Assert.IsTrue(interp.IsStale);
            Assert.IsTrue(interp.Bodies[0].Stale);
            double frozen = interp.Bodies[0].PosX;
            Assert.AreEqual(1.0 * (2 * StepSeconds + 0.25), frozen, 1e-5);
            interp.Update(70);
            Assert.AreEqual(frozen, interp.Bodies[0].PosX, 1e-12, "frozen");
            Assert.IsTrue(interp.IsStale);
        }

        [Test]
        public void LateArrivalAfterFreeze_DoesNotTeleport_AndBlendsOverAtLeast250ms()
        {
            SnapshotInterpolator interp;
            const double speed = 3.0;
            // Host path x = 3 t. Snapshots up to host step 6 arrive on time (arrival = host + 2); outage until host step 40.
            interp = new SnapshotInterpolator(StepSeconds, SnapshotEvery);
            var pending = new SortedDictionary<long, StateSnapshot>();
            for (long h = 0; h <= 160; h += 2) if (h <= 6 || h >= 40) pending[h + 2] = Line(h, speed);
            double prev = double.NaN, maxMove = 0, errAtResume = -1, errLater = -1; long resume = -1; bool stale = false;
            for (int n = 0; n <= 160; n++)
            {
                if (pending.TryGetValue(n, out var s)) { interp.Receive(n, s); if (resume < 0 && s.HostStep >= 40) resume = n; }
                interp.Update(n);
                stale |= interp.IsStale;
                if (interp.Bodies.Count == 0) continue;
                var b = interp.Bodies[0];
                if (!double.IsNaN(prev)) maxMove = Math.Max(maxMove, Math.Abs(b.PosX - prev));
                prev = b.PosX;
                double err = Math.Abs(b.PosX - speed * interp.RenderStep * StepSeconds);
                if (n == resume) errAtResume = err;
                if (resume >= 0 && n == resume + 12) errLater = err; // 0.24 s later
            }
            Assert.IsTrue(stale);
            Assert.Greater(errAtResume, 0.5, "the frozen body was well behind the truth");
            Assert.Less(maxMove, 0.2, "no teleport (a jump would be the full error)");
            Assert.Greater(errLater / errAtResume, 0.1, "blend takes >= 250 ms to mostly finish");
            Assert.Less(errLater / errAtResume, 0.35);
            Assert.Less(Math.Abs(prev - speed * interp.RenderStep * StepSeconds), 1e-4, "converged");
            Assert.IsFalse(interp.IsStale);
        }

        [Test]
        public void YawInterpolation_TakesShortWayAcrossPi()
        {
            var interp = new SnapshotInterpolator(StepSeconds, SnapshotEvery);
            float a = (float)(Math.PI - 0.01), b = (float)(-Math.PI + 0.01);
            interp.Receive(0, new StateSnapshot { HostStep = 0, Entities = new[] { new EntityState(Id1, 0, 0, 0, 0, 0, 0, a, 0) } });
            interp.Receive(0, new StateSnapshot { HostStep = 100, Entities = new[] { new EntityState(Id1, 0, 0, 0, 0, 0, 0, b, 0) } });
            interp.Update(56); // render step 50
            Assert.AreEqual(Math.PI, Math.Abs(interp.Bodies[0].Yaw), 0.011);
        }
    }
}
