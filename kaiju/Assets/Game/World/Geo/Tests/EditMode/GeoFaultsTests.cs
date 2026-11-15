// T-036c: the baked `fault_id` is carried end to end: tile FaultsLayer, the global loader, and a tampered layer is refused.
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Game.World.Geo.Tests
{
    public class GeoFaultsTests
    {
        static string GlobalFaults()
        {
            string p = Path.Combine(GeoTestData.ProjectRoot(), "Assets", "StreamingAssets", "Geo", "global", "v1", "faults_bay.geojson");
            Assert.IsTrue(File.Exists(p), p);
            return p;
        }

        [Test]
        public void GlobalLoader_ReadsEveryFeature_WithBakedFaultIdAndSection()
        {
            var faults = GeoFaultsLoader.LoadGlobal(GlobalFaults());
            Assert.AreEqual(3232, faults.Count);
            Assert.IsTrue(faults.All(f => f.FaultId != null), "every baked feature has a fault_id");
            Assert.IsTrue(faults.All(f => f.Lines.Count > 0));
            var first = faults[0];
            Assert.AreEqual("qf:4:59", first.Id);
            Assert.AreEqual("1", first.FaultId);
            Assert.AreEqual("San Andreas fault zone", first.FaultName);
        }

        [Test]
        public void GlobalLoader_RefusesATamperedLayer_ButNotWhenVerifyIsOff()
        {
            string dir = Path.Combine(Path.GetTempPath(), "geo_faults_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string src = GlobalFaults();
            string dst = Path.Combine(dir, "faults_bay.geojson");
            File.WriteAllText(dst, File.ReadAllText(src) + " ");
            File.Copy(Path.ChangeExtension(src, null) + ".manifest.json", Path.Combine(dir, "faults_bay.manifest.json"));
            Assert.Throws<FormatException>(() => GeoFaultsLoader.LoadGlobal(dst));
            Assert.AreEqual(3232, GeoFaultsLoader.LoadGlobal(dst, verifyHash: false).Count);
        }

        [Test]
        public void TileFaultsLayer_CarriesFaultId()
        {
            string dir = Path.Combine(Path.GetTempPath(), "geo_tilefaults_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            foreach (string f in Directory.GetFiles(GeoTestData.M0Dir)) File.Copy(f, Path.Combine(dir, Path.GetFileName(f)));
            File.WriteAllText(Path.Combine(dir, "faults.geojson"),
                "{\"type\":\"FeatureCollection\",\"features\":[{\"type\":\"Feature\",\"id\":\"qf:9:1\",\"properties\":{\"fault_id\":\"77\",\"fault_name\":\"Test fault\",\"section\":\"S\",\"slip_sense\":\"Right lateral\"}," +
                "\"geometry\":{\"type\":\"LineString\",\"coordinates\":[[61500.0,73800.0],[61600.0,73900.0]]}}]}");
            var ctx = new GeoTileContext(dir, GeoTestData.Settings(), verifyHashes: false);
            var o = new FaultsLayer().Build(ctx);
            Assert.AreEqual(1, o.Faults.Count);
            Assert.AreEqual("77", o.Faults[0].FaultId);
            Assert.AreEqual("qf:9:1", o.Faults[0].Id);
            Assert.AreEqual(1, o.Faults[0].Lines.Count);
        }
    }
}
