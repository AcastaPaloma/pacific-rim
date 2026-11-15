// T-039b headless smoke: opens M1Slice in Play Mode, runs the scripted sequence (quake, impactor, kaiju chase) and writes evidence/T-039b/smoke_editor.json.
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
            string outPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), "evidence", "T-039b", "smoke_editor.json");
            SliceBootstrap.SmokeFrames = 600; SliceBootstrap.SmokeOut = outPath;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Game/Slice/Scenes/M1Slice.unity", new LoadSceneParameters(LoadSceneMode.Single));
            float t = 0f;
            while ((SliceBootstrap.Instance == null || !SliceBootstrap.Instance.SmokeDone) && t < 800f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.IsNotNull(SliceBootstrap.Instance, "bootstrap present");
            Assert.IsTrue(SliceBootstrap.Instance.SmokeDone, "smoke finished (loaded: " + SliceBootstrap.Instance.Ready + ")");
            Assert.IsTrue(File.Exists(outPath));
            SliceBootstrap.SmokeFrames = 0;
        }
    }
}
