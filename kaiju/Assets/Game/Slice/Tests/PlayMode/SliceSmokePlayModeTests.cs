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
            SliceBootstrap.SmokeFrames = 600; SliceBootstrap.SmokeOut = outPath;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Game/Slice/Scenes/M1Slice.unity", new LoadSceneParameters(LoadSceneMode.Single));
            float t = 0f;
            while ((SliceBootstrap.Instance == null || !SliceBootstrap.Instance.SmokeDone) && t < 800f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.IsNotNull(SliceBootstrap.Instance, "bootstrap present");
            Assert.IsTrue(SliceBootstrap.Instance.SmokeDone, "smoke finished (loaded: " + SliceBootstrap.Instance.Ready + ")");
            Assert.IsTrue(File.Exists(outPath));
            string json = SliceBootstrap.Instance.SmokeJson;
            StringAssert.Contains("\"quakes\": 1", json); StringAssert.Contains("\"impacts\": 1", json);
            StringAssert.Contains("\"tiles\": 15", json);
            SliceBootstrap.SmokeFrames = 0;
        }
    }
}
