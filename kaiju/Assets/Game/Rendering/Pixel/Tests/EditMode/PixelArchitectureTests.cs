using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Game.Rendering.Pixel.Tests
{
    /// <summary>Engine-free architecture pins (DEF-T004-arch-1, arch-4): asmdef references and feature-flag files.</summary>
    public class PixelArchitectureTests
    {
        [Test]
        public void PixelAsmdefs_ReferenceNoCameraUnitOrWorldAssembly_AndNoGuids()
        {
            string dir = Path.Combine(PixelTestPaths.ProjectRoot(), "Assets", "Game", "Rendering", "Pixel");
            string[] files = Directory.GetFiles(dir, "Game.Rendering.Pixel*.asmdef", SearchOption.AllDirectories);
            Assert.GreaterOrEqual(files.Length, 4, "expected the Pixel asmdefs to be found");
            foreach (string file in files)
            {
                string text = File.ReadAllText(file);
                foreach (string banned in new[] { "Game.Cameras", "Game.Camera", "Game.Units", "Game.World" })
                    StringAssert.DoesNotContain("\"" + banned, text, file);
                StringAssert.DoesNotContain("GUID:", text, file + ": reference assemblies by name");
            }
        }

        static void AssertFlagFile(string fileName, string[] expected)
        {
            string path = Path.Combine(PixelTestPaths.ProjectRoot(), "Assets", "Game", "Config", "FeatureFlags", fileName);
            string text = File.ReadAllText(path);
            var root = (MiniJson.Obj)MiniJson.Parse(text);
            var flags = root.Object("flags");
            CollectionAssert.AreEquivalent(expected, new List<string>(flags.Keys), fileName + " keys drifted from the flag constants");
            foreach (var kv in flags) Assert.AreEqual(false, kv.Value, fileName + ": " + kv.Key + " must ship false");
        }

        [Test]
        public void PixelFlagFile_AllFalse_AndMatchesPixelFlagsAll() => AssertFlagFile("Rendering.Pixel.json", PixelFlags.All);

        [Test]
        public void CameraFlagFile_AllFalse_AndMatchesCameraFlagsAll() =>
            // Literal copy of CameraFlags.All (Assets/Game/Camera/Contracts/CameraContracts.cs): not compiled into this project.
            AssertFlagFile("Camera.json", new[] { "camera.thirdPersonRig", "camera.snapPosition", "camera.quantiseRotation", "camera.footfallImpulse" });
    }
}
