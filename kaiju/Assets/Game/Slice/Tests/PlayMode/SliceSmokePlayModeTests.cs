// T-039b headless smoke, T-039c: opens M1Slice in Play Mode, runs the scripted sequence (quake, impactor, kaiju chase, captures) and writes evidence/T-039c/smoke_editor.json.
// The Play Mode run proves the scene boots and the logic counters; the pixel check that matters runs in the macOS player (smoke_player_mac.json): the Game view of a batch-mode editor is not what a player presents.
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Slice.PlayMode.Tests
{
    public class SliceSmokePlayModeTests
    {
        [UnityTest, Timeout(900000)]
        public IEnumerator Slice_LoadsQuakesAndImpacts()
        {
            string outPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), "evidence", "T-039c", "smoke_editor.json");
            if (File.Exists(outPath)) File.Delete(outPath);   // a stale file can never stand in for this run (DEF-M1slice-f2)
            SliceBootstrap.SmokeFrames = 600; SliceBootstrap.SmokeOut = outPath; SliceBootstrap.ConfigName = "m1";   // the lean T-039b set; the feel check has its own test
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Game/Slice/Scenes/M1Slice.unity", new LoadSceneParameters(LoadSceneMode.Single));
            float t = 0f;
            while ((SliceBootstrap.Instance == null || !SliceBootstrap.Instance.SmokeDone) && t < 800f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.IsNotNull(SliceBootstrap.Instance, "bootstrap present");
            Assert.IsTrue(SliceBootstrap.Instance.SmokeDone, "smoke finished (loaded: " + SliceBootstrap.Instance.Ready + ")");
            Assert.IsTrue(File.Exists(outPath));
            string json = SliceBootstrap.Instance.SmokeJson;
            StringAssert.Contains("\"quakes\": 1", json); StringAssert.Contains("\"impacts\": 1", json);
            StringAssert.Contains("\"tiles\": 15", json);
            SliceBootstrap.SmokeFrames = 0; SliceBootstrap.ConfigName = null;
        }

        // T-189: the full feel-check flag set (combat, hangar, audio.slice, emergence on top of the M1 set) boots, fights (cannon + arm + kaiju melee), runs a hangar cycle and exits clean.
        [UnityTest, Timeout(900000)]
        public IEnumerator FeelCheck_BootsFightsAndExitsClean()
        {
            string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "evidence", "T-189");
            string outPath = Path.Combine(dir, "smoke_feelcheck.json");
            if (File.Exists(outPath)) File.Delete(outPath);
            // no SmokeOut: the screenshot captures of the pixel check hang in a batch-mode editor (the player run, T-039c, owns that check); the test writes the summary itself
            SliceBootstrap.SmokeFrames = 600; SliceBootstrap.SmokeOut = null; SliceBootstrap.ConfigName = "feelcheck";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Game/Slice/Scenes/M1Slice.unity", new LoadSceneParameters(LoadSceneMode.Single));
            float t = 0f;
            while ((SliceBootstrap.Instance == null || !SliceBootstrap.Instance.SmokeDone) && t < 800f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.IsNotNull(SliceBootstrap.Instance, "bootstrap present");
            Assert.IsTrue(SliceBootstrap.Instance.SmokeDone, "smoke finished (loaded: " + SliceBootstrap.Instance.Ready + ")");
            string json = SliceBootstrap.Instance.SmokeJson;
            Directory.CreateDirectory(dir); File.WriteAllText(outPath, json);
            StringAssert.Contains("\"config\": \"feelcheck\"", json);
            StringAssert.Contains("\"quakes\": 1", json); StringAssert.Contains("\"impacts\": 1", json); StringAssert.Contains("\"tiles\": 15", json);
            StringAssert.Contains("\"combat\": true", json); StringAssert.Contains("\"hangar\": true", json); StringAssert.Contains("\"hangar_cycles\": 1", json);
            StringAssert.DoesNotContain("\"cannon_shots\": 0,", json); StringAssert.DoesNotContain("\"weapon_fired_events\": 0,", json); StringAssert.DoesNotContain("\"impact_impulse_events\": 0,", json);
            StringAssert.DoesNotContain("\"impacts_kaiju\": 0,", json); StringAssert.DoesNotContain("\"kaiju_min_hp\": 400.0", json);
            StringAssert.DoesNotContain("\"audio\": null", json); StringAssert.DoesNotContain("\"received\": 0,", json);
            SliceBootstrap.SmokeFrames = 0; SliceBootstrap.ConfigName = null;
        }

        // T-200: the wake-up demo profile: physics mech (walk_mode 2) walks, kaiju spawned, buildings damaged. Same checks as -sliceSmoke N -sliceConfig demo in a player.
        [UnityTest, Timeout(1200000)]
        public IEnumerator Demo_PhysicsMechWalksAndFights()
        {
            string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "evidence", "T-200");
            string outPath = Path.Combine(dir, "smoke_demo.json");
            if (File.Exists(outPath)) File.Delete(outPath);
            SliceBootstrap.SmokeFrames = 1200; SliceBootstrap.SmokeOut = null; SliceBootstrap.ConfigName = "demo";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Game/Slice/Scenes/M1Slice.unity", new LoadSceneParameters(LoadSceneMode.Single));
            float t = 0f;
            while ((SliceBootstrap.Instance == null || !SliceBootstrap.Instance.SmokeDone) && t < 1100f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.IsNotNull(SliceBootstrap.Instance, "bootstrap present");
            Assert.IsTrue(SliceBootstrap.Instance.SmokeDone, "smoke finished (loaded: " + SliceBootstrap.Instance.Ready + ")");
            string json = SliceBootstrap.Instance.SmokeJson;
            Directory.CreateDirectory(dir); File.WriteAllText(outPath, json);
            StringAssert.Contains("\"config\": \"demo\"", json);
            StringAssert.Contains("\"mech_body\": \"physics walk_mode 2\"", json);
            var m = System.Text.RegularExpressions.Regex.Match(json, "\"mech_travel_m\": ([0-9.]+)");
            Assert.IsTrue(m.Success && double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) > 0.0, "mech travel > 0 m: " + m.Value);
            StringAssert.DoesNotContain("\"building_damaged_events\": 0,", json);
            StringAssert.DoesNotContain("\"decisions\": 0,", json);
            SliceBootstrap.SmokeFrames = 0; SliceBootstrap.ConfigName = null;
        }
    }
}
