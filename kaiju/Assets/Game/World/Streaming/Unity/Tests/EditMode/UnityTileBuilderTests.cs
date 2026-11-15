// T-109: Unity-side bookkeeping (EditMode): tile roots at (tile origin - render origin), exact rebase shifts, collider bake attach,
// release, and the world.streaming flag gate.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Game.Core;
using Game.World.Geo;
using Game.World.Geo.Unity;
using Game.World.Streaming.Host;

namespace Game.World.Streaming.Unity.Tests
{
    public class UnityTileBuilderTests
    {
        sealed class Sink : IBuildSink
        {
            public readonly List<ResidencyKey> Built = new List<ResidencyKey>(); public readonly List<string> Failed = new List<string>();
            void IBuildSink.Built(in ResidencyKey key, long g, long c) { Built.Add(key); }
            void IBuildSink.BuildFailed(in ResidencyKey key, string reason) { Failed.Add(reason); }
        }
        sealed class Flags : IFeatureFlags
        {
            readonly bool _on; public Flags(bool on) { _on = on; }
            public bool IsOn(string n) => _on && n == StreamingFlags.Enabled;
            public int Count => 1; public string NameAt(int i) => StreamingFlags.Enabled; public bool ValueAt(int i) => _on;
        }

        GameObject _parent; UnityTileBuilder _b;
        [SetUp] public void Up()
        {
            _parent = new GameObject("test_stream");
            _b = new UnityTileBuilder(_parent.transform, new InlineWorkRunner(), GeoTileBuilder.LoadSettings(Application.dataPath));
        }
        [TearDown] public void Down() { _b.ReleaseAll(); Object.DestroyImmediate(_parent); }

        static TilePayload Quad(ResidencyKey key, string material = "terrain")
        {
            var p = new TilePayload { Key = key };
            p.Meshes.Add(new GeoMeshData { Name = "q", Material = material, Positions = new float[] { 0, 0, 0, 0, 0, 256, 256, 0, 0, 256, 0, 256 }, Indices = new[] { 0, 1, 2, 2, 1, 3 } });
            return p;
        }

        [Test]
        public void RenderRoot_SitsAtTileOriginMinusRenderOrigin_AndRebaseShiftsByExactIntegers()
        {
            _b.ShiftOrigin(61440, 73728);
            var key = ResidencyKey.Render(GeoTileId.From(0, 31, 36));
            var s = new Sink();
            _b.Build(key, Quad(key), s);
            Assert.AreEqual(1, s.Built.Count);
            _b.TryGetRoot(key, out var t);
            Assert.AreEqual(new Vector3(2048f, 0f, 0f), t.localPosition);
            _b.ShiftOrigin(63488, 73728);
            Assert.AreEqual(new Vector3(0f, 0f, 0f), t.localPosition);
            _b.ShiftOrigin(63488 + 4096 * 3, 73728 - 2048);
            Assert.AreEqual(new Vector3(-12288f, 0f, 2048f), t.localPosition);
            Assert.AreEqual(0.0, _b.OriginX % 2048.0);
            Assert.Throws<System.ArgumentException>(() => _b.ShiftOrigin(100, 0));
        }

        [Test]
        public void PhysicsChunk_ColliderIsAttachedAfterBake_AndReleasedWithItsKey()
        {
            _b.ShiftOrigin(0, 0);
            var key = ResidencyKey.PhysicsChunk(GeoTileId.From(0, 1, 1), 2, 3);
            var s = new Sink();
            _b.Build(key, Quad(key), s);
            Assert.AreEqual(0, s.Built.Count);               // not resident until the bake is attached
            _b.Tick();
            Assert.AreEqual(1, s.Built.Count, string.Join(";", s.Failed));
            _b.TryGetRoot(key, out var t);
            Assert.AreEqual(new Vector3(2048f, 0f, 2048f), t.localPosition);   // tile corner: payload meshes are tile-local
            var col = t.GetComponentInChildren<MeshCollider>();
            Assert.IsNotNull(col); Assert.IsNotNull(col.sharedMesh);
            Assert.IsNull(t.GetComponentInChildren<MeshRenderer>());           // colliders only inside the physics ring
            _b.Release(key);
            Assert.AreEqual(0, _b.ResidentNodes);
        }

        [Test]
        public void ReleasedBeforeBakeFinishes_NeverReportsBuilt()
        {
            var key = ResidencyKey.PhysicsChunk(GeoTileId.From(0, 0, 0), 0, 0);
            var s = new Sink();
            _b.Build(key, Quad(key), s);
            _b.Release(key);
            _b.Tick();
            Assert.AreEqual(0, s.Built.Count);
        }

        [Test]
        public void Install_IsInertWhenFlagOff_AndCreatesHostWhenOn()
        {
            Assert.IsNull(StreamingModule.Install(new Flags(false)));
            var host = StreamingModule.Install(new Flags(true));
            Assert.IsNotNull(host);
            Object.DestroyImmediate(host.gameObject);
        }
    }
}
