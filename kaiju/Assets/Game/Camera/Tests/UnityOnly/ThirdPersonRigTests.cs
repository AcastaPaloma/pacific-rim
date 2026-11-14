using System;
using System.Collections.Generic;
using Game.Core;
using Game.Rendering.Pixel;
using NUnit.Framework;
using UnityEngine;

namespace Game.Cameras.Tests
{
    /// <summary>
    /// Rig tests with the engine (EditMode). The pixel grid comes from a stub of the Rendering.Pixel contract and the
    /// impulse from a real Core event bus: no pipeline, no mech code.
    /// </summary>
    public class ThirdPersonRigTests
    {
        sealed class GridStub : IPixelGridSource
        {
            public bool On = true;
            public bool TryGetGrid(out PixelGrid grid)
            {
                double tanH = Math.Tan(30.0 * Math.PI / 180.0), tanV = tanH * 270.0 / 480.0;
                grid = new PixelGrid { InternalWidth = 480, InternalHeight = 270, Scale = 4, TanHalfHorizontalFov = (float)tanH, TanHalfVerticalFov = (float)tanV, TexelAngle = (float)PixelScaleRule.TexelAngle(tanV, 270) };
                return On;
            }
        }

        sealed class MovingTarget : ICameraTarget
        {
            public Vector3 Ground; public float Height = 80f;
            public bool TryGetTarget(out CameraVec3 groundPosition, out float heightMetres) { groundPosition = new CameraVec3(Ground.x, Ground.y, Ground.z); heightMetres = Height; return true; }
        }

        GameObject m_Go; ThirdPersonRig m_Rig; GridStub m_Grid; readonly ThirdPersonRigSettings m_Settings = new ThirdPersonRigSettings();
        const float Dt = 1f / 60f;

        [SetUp]
        public void SetUp()
        {
            m_Go = new GameObject("rig-test-camera"); m_Go.AddComponent<Camera>();
            m_Rig = m_Go.AddComponent<ThirdPersonRig>(); m_Grid = new GridStub();
            m_Rig.SetGridSource(m_Grid);
            m_Rig.Configure(m_Settings, true, true, true);
        }

        [TearDown]
        public void TearDown() { UnityEngine.Object.DestroyImmediate(m_Go); }

        /// <summary>Where a world point appears, in internal texels from the screen centre, in the presented image (rendered pose plus the sub-texel shift).</summary>
        Vector2 PresentedTexel(Vector3 world)
        {
            m_Grid.TryGetGrid(out PixelGrid g); m_Rig.TryGetHints(out PixelStabilisationHints h);
            Vector3 v = m_Go.transform.InverseTransformPoint(world);
            float x = v.x / v.z / g.TanHalfHorizontalFov * 0.5f * g.InternalWidth, y = v.y / v.z / g.TanHalfVerticalFov * 0.5f * g.InternalHeight;
            // The present stage shows texel t + shift: content appears shifted by minus the remainder, in whole output pixels.
            return new Vector2(x - Mathf.Round(h.SubTexelX * g.Scale) / g.Scale, y - Mathf.Round(h.SubTexelY * g.Scale) / g.Scale);
        }

        [Test]
        public void Rig_ImplementsTheContracts_AndFramesAnEightyMetreTarget()
        {
            Assert.IsInstanceOf<ICameraRig>(m_Rig); Assert.IsInstanceOf<IPixelStabilisationSource>(m_Rig);
            Assert.AreEqual("ThirdPersonRig", m_Rig.RigId);
            var target = new MovingTarget { Ground = new Vector3(100, 0, 200) };
            m_Rig.SetTarget(target); m_Rig.Cut(); m_Rig.Tick(Dt);
            Vector3 top = PresentedTexelWorld(target.Ground + Vector3.up * 80f), bottom = PresentedTexelWorld(target.Ground);
            float heightShare = (top.y - bottom.y) / 270f;
            Assert.AreEqual(0.5f, heightShare, 0.08f, "an 80 m target fills about half the frame height");
            Assert.Less(m_Go.transform.position.y, target.Ground.y + 80f * m_Settings.focusHeightFraction, "the camera sits below the focus and looks up: low horizon");
            Assert.IsTrue(m_Rig.TryGetHints(out var hints)); Assert.AreEqual(4, hints.PixelScale); Assert.Greater(hints.PositionGrid, 0f);
            Assert.AreEqual(80f * m_Settings.distanceInTargetHeights, hints.ReferenceDepth, 1e-3, "the snap locks the plane of the target");
        }

        Vector3 PresentedTexelWorld(Vector3 world) { Vector2 p = PresentedTexel(world); return new Vector3(p.x, p.y, 0); }

        [Test]
        public void FollowingAMovingTarget_KeepsItSteadyWithinOneInternalTexel()
        {
            var target = new MovingTarget { Ground = new Vector3(0, 0, 0) };
            m_Rig.SetTarget(target); m_Rig.SetOrbitYaw(90f); m_Rig.Cut(); m_Rig.Tick(Dt);
            var xs = new List<float>(); var ys = new List<float>();
            for (int i = 0; i < 600; i++)
            {
                target.Ground += new Vector3(0, 0, 10f * Dt);   // 10 m/s across the view (the camera looks along +X)
                m_Rig.Tick(Dt);
                Vector2 p = PresentedTexel(target.Ground + Vector3.up * 44f);
                xs.Add(p.x); ys.Add(p.y);
            }
            // After the follow lag has settled the target holds its place on screen; frame-to-frame jitter stays under a texel.
            float maxStep = 0, maxJerk = 0;
            for (int i = 302; i < xs.Count; i++)
            {
                maxStep = Mathf.Max(maxStep, Mathf.Abs(xs[i] - xs[i - 1]), Mathf.Abs(ys[i] - ys[i - 1]));
                maxJerk = Mathf.Max(maxJerk, Mathf.Abs(xs[i] - 2 * xs[i - 1] + xs[i - 2]));
            }
            Assert.Less(maxStep, 1f, "target screen position moves less than one internal texel per frame");
            Assert.Less(maxJerk, 1f, "no jitter beyond one internal texel");
            float settled = xs[xs.Count - 1];
            for (int i = 400; i < xs.Count; i++) Assert.Less(Mathf.Abs(xs[i] - settled), 1.5f, "steady framing at constant speed (frame " + i + ")");
        }

        [Test]
        public void WithoutAGrid_TheRigOffersNoHints_AndWithStabilisersOff_ItRendersTheWantedPose()
        {
            m_Grid.On = false;
            m_Rig.SetScriptedPose(new CameraPose { Position = new CameraVec3(1.23f, 70.4f, -9.87f), YawDeg = 12.3f, PitchDeg = 3.3f }, 600f);
            m_Rig.Tick(Dt);
            Assert.IsFalse(m_Rig.TryGetHints(out _));
            Assert.AreEqual(1.23f, m_Go.transform.position.x, 1e-4);
            m_Grid.On = true; m_Rig.Configure(m_Settings, false, false, false); m_Rig.Tick(Dt);
            Assert.IsTrue(m_Rig.TryGetHints(out var h)); Assert.AreEqual(0f, h.SubTexelX); Assert.AreEqual(0f, h.PositionGrid);
            Assert.AreEqual(12.3f, m_Go.transform.eulerAngles.y, 1e-3); Assert.AreEqual(-3.3f, Mathf.DeltaAngle(0, m_Go.transform.eulerAngles.x), 1e-3);
        }

        [Test]
        public void ACut_RendersExactlyTheStaticPose_NoResidualState()
        {
            var a = new CameraPose { Position = new CameraVec3(0, 70, -150), YawDeg = 0, PitchDeg = 3 };
            var b = new CameraPose { Position = new CameraVec3(127.3f, 1.7f, -109.8f), YawDeg = 31.7f, PitchDeg = 7.3f };
            for (int i = 0; i < 30; i++) { a.Position.X += 0.4f; a.YawDeg += 0.07f; m_Rig.SetScriptedPose(a, 600f); m_Rig.Tick(Dt); }
            m_Rig.Cut(); m_Rig.SetScriptedPose(b, 600f); m_Rig.Tick(Dt);
            Vector3 cutPosition = m_Go.transform.position; Quaternion cutRotation = m_Go.transform.rotation; m_Rig.TryGetHints(out var cutHints);
            Assert.IsTrue(cutHints.Cut);
            m_Rig.Tick(Dt); m_Rig.Tick(Dt);   // the same pose held: a static capture of the new pose
            m_Rig.TryGetHints(out var held);
            Assert.AreEqual(b.Position.X, cutPosition.x, 1e-4, "a cut renders the wanted position");
            Assert.AreEqual(cutPosition, m_Go.transform.position); Assert.AreEqual(cutRotation, m_Go.transform.rotation);
            Assert.AreEqual(held.SubTexelX, cutHints.SubTexelX); Assert.AreEqual(held.SubTexelY, cutHints.SubTexelY);
            Assert.AreEqual(held.DitherOriginX, cutHints.DitherOriginX); Assert.AreEqual(held.DitherOriginY, cutHints.DitherOriginY);
            Assert.IsFalse(held.Cut);
        }

        [Test]
        public void FootfallEvent_OnTheCoreBus_MovesTheCamera_InProportion_Bounded_AndDecaysBack()
        {
            var bus = new EventBus(new CollectingErrorSink(), 10000);
            m_Rig.Subscribe(bus);
            Assert.AreEqual(1, bus.SubscriberCount<CameraShakeImpulse>());
            var pose = new CameraPose { Position = new CameraVec3(0, 70, -150), YawDeg = 0, PitchDeg = 3 };
            m_Rig.Configure(m_Settings, false, false, true);   // stabilisers off: read the raw motion
            m_Rig.SetScriptedPose(pose, 600f); m_Rig.Cut(); m_Rig.Tick(Dt);
            float restY = m_Go.transform.position.y;

            float Peak(float impulseNs)
            {
                m_Rig.Cut(); m_Rig.Tick(Dt);
                bus.Publish(new CameraShakeImpulse { Source = default, OriginX = 0, OriginY = 0, OriginZ = -100, ImpulseNs = impulseNs });
                float peak = 0;
                for (int i = 0; i < 240; i++) { m_Rig.Tick(Dt); peak = Mathf.Max(peak, Mathf.Abs(m_Go.transform.position.y - restY)); }
                return peak;
            }
            float small = Peak(5e6f), twice = Peak(1e7f), huge = Peak(1e12f);
            Assert.Greater(small, 0.02f, "a footfall moves the camera");
            Assert.AreEqual(2f, twice / small, 0.02f, "in proportion to the impulse");
            Assert.LessOrEqual(huge, m_Settings.impulseMaxMetres + 1e-4f, "bounded");
            for (int i = 0; i < 240; i++) m_Rig.Tick(Dt);
            Assert.AreEqual(restY, m_Go.transform.position.y, "exactly back at rest after the decay");

            // Reproducible: the same event at the same time gives the same motion.
            float again = Peak(1e7f); Assert.AreEqual(twice, again);
            // With snapping on, framing returns to the identical snapped pose once the impulse has decayed.
            m_Rig.Configure(m_Settings, true, true, true); m_Rig.Cut(); m_Rig.Tick(Dt);
            Vector3 snapped = m_Go.transform.position;
            bus.Publish(new CameraShakeImpulse { OriginX = 0, OriginY = 0, OriginZ = -100, ImpulseNs = 3e7f });
            bool moved = false;
            for (int i = 0; i < 300; i++) { m_Rig.Tick(Dt); moved |= m_Go.transform.position != snapped || HintsRemainder() != 0f; }
            // The snap steps along the camera axes of the moment, so the path back is not bit-identical: framing returns
            // to within a small fraction of a texel (one texel is 1.44 m at the 600 m reference depth) and then holds still.
            Assert.IsTrue(moved); Assert.Less(Vector3.Distance(snapped, m_Go.transform.position), 0.05f, "framing returns to within a twentieth of a texel");
            Vector3 settled = m_Go.transform.position; m_Rig.Tick(Dt); m_Rig.Tick(Dt); Assert.AreEqual(settled, m_Go.transform.position, "and is exactly static afterwards");
            snapped = settled;
            // Flag off: events are ignored. Unsubscribed: no handler left on the bus.
            m_Rig.Configure(m_Settings, true, true, false); m_Rig.Cut(); m_Rig.Tick(Dt);
            snapped = m_Go.transform.position;
            bus.Publish(new CameraShakeImpulse { ImpulseNs = 3e7f }); m_Rig.Tick(Dt); m_Rig.Tick(Dt);
            Assert.AreEqual(snapped, m_Go.transform.position);
            m_Rig.Unsubscribe(); Assert.AreEqual(0, bus.SubscriberCount<CameraShakeImpulse>());
        }

        float HintsRemainder() { m_Rig.TryGetHints(out var h); return h.SubTexelY; }

        [Test]
        public void Bind_WiresFlagsBusAndRegistry_ThroughCoreContractsOnly()
        {
            var registry = new ServiceRegistry();
            var bus = new EventBus(new CollectingErrorSink(), 10000);
            registry.Register<IEventBus>(bus);
            registry.Register<IFeatureFlags>(new FeatureFlagSnapshot(new[] { new KeyValuePair<string, bool>(CameraFlags.SnapPosition, true), new KeyValuePair<string, bool>(CameraFlags.QuantiseRotation, false), new KeyValuePair<string, bool>(CameraFlags.FootfallImpulse, true) }));
            m_Rig.Bind(registry);
            Assert.IsTrue(m_Rig.SnapPosition); Assert.IsFalse(m_Rig.QuantiseRotation);
            Assert.AreSame(m_Rig, registry.Resolve<ICameraRig>()); Assert.AreSame(m_Rig, registry.Resolve<IPixelStabilisationSource>());
            Assert.AreEqual(1, bus.SubscriberCount<CameraShakeImpulse>());
        }
    }
}
