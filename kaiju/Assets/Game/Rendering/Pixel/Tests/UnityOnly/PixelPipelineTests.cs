using System;
using System.Collections.Generic;
using System.IO;
using Game.Rendering.Pixel.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace Game.Rendering.Pixel.Tests
{
    /// <summary>
    /// EditMode tests that render through the installed pixel pipeline (capture tests, MODULARITY §5 step 5). The scene
    /// is built in memory from primitives; there is no camera rig here, so every frame is also the "rig gives no hints"
    /// case of the reconciled spec (C17).
    /// </summary>
    public class PixelPipelineTests
    {
        const int OutW = 480, OutH = 272;   // 68 internal lines at scale 4: small and fast
        PixelPipelineCatalog m_Catalog; PixelPipelineScope m_Scope; Camera m_World, m_Present; PixelCameraBinder m_Binder; RenderTexture m_Output;
        PixelPipelineSettings m_Settings; readonly List<UnityEngine.Object> m_Temp = new List<UnityEngine.Object>();
        static readonly string[] M0 = { PixelFlags.Enabled, PixelFlags.LowRes, PixelFlags.Dither, PixelFlags.Haze, PixelFlags.Quantise, PixelFlags.SubPixelPresent };

        [SetUp]
        public void SetUp()
        {
            m_Catalog = AssetDatabase.LoadAssetAtPath<PixelPipelineCatalog>(PixelPipelineInstaller.CatalogPath);
            Assert.IsNotNull(m_Catalog, "run PixelPipelineInstaller.InstallAll");
            m_Scope = new PixelPipelineScope(m_Catalog.pipeline);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            Shape(PrimitiveType.Plane, new Vector3(0, 0, 0), new Vector3(4000, 1, 4000), unlit, 0.25f);
            Shape(PrimitiveType.Cube, new Vector3(0, 40, 150), new Vector3(30, 80, 20), unlit, 0.12f);
            Shape(PrimitiveType.Cube, new Vector3(300, 150, 3000), new Vector3(900, 300, 100), unlit, 0.2f);
            Shape(PrimitiveType.Sphere, new Vector3(-60, 20, 120), new Vector3(40, 40, 40), unlit, 0.5f);
            m_World = NewCamera("world", m_Catalog.worldRendererIndex, true);
            m_World.transform.SetPositionAndRotation(new Vector3(0, 30, -100), Quaternion.Euler(-4, 0, 0));
            m_Binder = m_World.gameObject.AddComponent<PixelCameraBinder>();
            m_Binder.catalog = m_Catalog; m_Binder.profile = m_Catalog.FindProfile("slate_teal"); m_Binder.outputSize = new Vector2Int(OutW, OutH);
            m_Settings = m_Catalog.LoadSettings();
            m_Present = NewCamera("present", m_Catalog.presentRendererIndex, false);
            m_Present.cullingMask = 0; m_Present.allowHDR = false;
            m_Present.gameObject.AddComponent<PixelPresentCamera>().source = m_Binder;
            m_Output = new RenderTexture(OutW, OutH, 24, GraphicsFormat.R8G8B8A8_UNorm) { filterMode = FilterMode.Point };   // URP requires a depth buffer on a camera output texture
            m_Output.Create();
            PixelLog.Reset();
            Configure(M0);
        }

        [TearDown]
        public void TearDown()
        {
            if (m_Output != null) { m_Output.Release(); UnityEngine.Object.DestroyImmediate(m_Output); }
            foreach (var o in m_Temp) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            m_Temp.Clear();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            m_Scope?.Dispose();
        }

        void Shape(PrimitiveType type, Vector3 position, Vector3 scale, Shader shader, float grey)
        {
            var go = GameObject.CreatePrimitive(type);
            go.transform.position = position; go.transform.localScale = scale;
            var material = new Material(shader); material.SetColor("_BaseColor", new Color(grey, grey, grey, 1f));
            go.GetComponent<MeshRenderer>().sharedMaterial = material; m_Temp.Add(material);
        }

        static Camera NewCamera(string name, int renderer, bool depth)
        {
            var go = new GameObject(name); var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 36f; cam.nearClipPlane = 0.5f; cam.farClipPlane = 60000f; cam.allowMSAA = false;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.SetRenderer(renderer); data.renderPostProcessing = false; data.antialiasing = AntialiasingMode.None; data.renderShadows = false; data.requiresDepthTexture = depth;
            return cam;
        }

        void Configure(IEnumerable<string> flags) => m_Binder.Configure(m_Settings, PixelFlagState.FromNames(flags));

        sealed class Frame { public int W, H; public Color32[] Internal; public Color32[] Output; public float[] Depth; }

        Frame Render()
        {
            m_Binder.PrepareFrame();
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = m_World.targetTexture };
            Assert.IsTrue(RenderPipeline.SupportsRenderRequest(m_World, request), "pixel pipeline is active");
            RenderPipeline.SubmitRenderRequest(m_World, request);
            m_Present.targetTexture = m_Output; request.destination = m_Output;
            RenderPipeline.SubmitRenderRequest(m_Present, request);
            m_Present.targetTexture = null;
            var layout = m_Binder.Layout; int g = layout.Guard;
            var f = new Frame { W = layout.Width, H = layout.Height };
            f.Internal = Read(m_Binder.InternalColour, new Rect(g, g, f.W, f.H), GraphicsFormat.R8G8B8A8_UNorm).GetPixels32();
            f.Output = Read(m_Output, new Rect(0, 0, OutW, OutH), GraphicsFormat.R8G8B8A8_UNorm).GetPixels32();
            f.Depth = Read(m_Binder.InternalDepth, new Rect(g, g, f.W, f.H), GraphicsFormat.R32_SFloat).GetRawTextureData<float>().ToArray();
            return f;
        }

        Texture2D Read(RenderTexture rt, Rect rect, GraphicsFormat format)
        {
            var tex = new Texture2D((int)rect.width, (int)rect.height, format, TextureCreationFlags.None); m_Temp.Add(tex);
            var previous = RenderTexture.active; RenderTexture.active = rt;
            tex.ReadPixels(rect, 0, 0, false); RenderTexture.active = previous;
            return tex;
        }

        static void AssertOnPalette(Frame f, PaletteProfile profile, string what)
        {
            var palette = profile.colours; int distinct = 0; var seen = new bool[256];
            for (int i = 0; i < f.Internal.Length; i++)
            {
                Color32 c = f.Internal[i];
                Assert.Less(c.a, palette.Length, what + ": index " + c.a + " at pixel " + i);
                Color32 p = palette[c.a];
                if (c.r != p.r || c.g != p.g || c.b != p.b) Assert.Fail(what + ": pixel " + i + " is " + c + " but palette[" + c.a + "] is " + p);
                if (!seen[c.a]) { seen[c.a] = true; distinct++; }
            }
            Assert.GreaterOrEqual(distinct, 3, what + ": a valid frame uses several palette colours");
        }

        [Test]
        public void M0Frame_EveryInternalPixelIsAnExactPaletteColour_WithItsIndex()
        {
            Frame f = Render();
            Assert.AreEqual(OutW / 4, f.W); Assert.AreEqual(OutH / 4, f.H);
            AssertOnPalette(f, m_Binder.profile, "M0");
            Assert.IsFalse(m_Binder.HasHints, "no rig: the pipeline renders unsnapped and still on palette");
            Assert.AreEqual(0, PixelLog.OrderViolations);
        }

        [Test]
        public void Output_IsAnExactIntegerPointUpscale()
        {
            Frame f = Render();
            for (int y = 0; y < OutH; y++)
                for (int x = 0; x < OutW; x++)
                {
                    Color32 o = f.Output[y * OutW + x], i = f.Internal[(y / 4) * f.W + (x / 4)];
                    if (o.r != i.r || o.g != i.g || o.b != i.b) Assert.Fail("output pixel " + x + "," + y + " = " + o + " differs from internal texel " + i);
                }
        }

        [Test]
        public void TwoRenders_AreBitIdentical()
        {
            Frame a = Render(), b = Render();
            CollectionAssert.AreEqual(a.Internal, b.Internal); CollectionAssert.AreEqual(a.Output, b.Output); CollectionAssert.AreEqual(a.Depth, b.Depth);
        }

        [Test]
        public void Depth_IsLinearEyeDepthInMetres()
        {
            Frame f = Render();
            // The centre texel looks along the camera axis (4 degrees up from 30 m): it hits the 80 m block's near face at z = 140.
            var t = m_World.transform; var ray = new Ray(t.position, t.forward);
            float hit = (140f - t.position.z) / ray.direction.z;                 // distance along the ray
            float expected = hit * Vector3.Dot(ray.direction, t.forward);        // = eye depth (ray is the forward axis)
            float sum = 0; int n = 0;
            for (int dy = -1; dy <= 0; dy++) for (int dx = -1; dx <= 0; dx++) { sum += f.Depth[(f.H / 2 + dy) * f.W + f.W / 2 + dx]; n++; }
            Assert.AreEqual(expected, sum / n, expected * 0.01f, "centre depth within 1%");
            Assert.Greater(f.Depth[(f.H - 1) * f.W + 2], 1e29f, "sky stores 1e30");
        }

        [Test]
        public void QuantiseOff_IsNotOnPalette_AndIndexIs255()
        {
            Configure(new[] { PixelFlags.Enabled, PixelFlags.LowRes, PixelFlags.Dither, PixelFlags.Haze });
            Frame f = Render();
            foreach (Color32 c in f.Internal) Assert.AreEqual(255, c.a);
            var palette = new HashSet<int>(); foreach (Color32 p in m_Binder.profile.colours) palette.Add((p.r << 16) | (p.g << 8) | p.b);
            int off = 0; foreach (Color32 c in f.Internal) if (!palette.Contains((c.r << 16) | (c.g << 8) | c.b)) off++;
            Assert.Greater(off, f.Internal.Length / 2, "an unquantised frame is mostly off palette");
        }

        [Test]
        public void EveryCombinationOfTheOptionalStages_RendersAValidOnPaletteFrame()
        {
            string[] optional = { PixelFlags.Haze, PixelFlags.Supersample, PixelFlags.Outline, PixelFlags.Dither };
            for (int mask = 0; mask < 16; mask++)
            {
                var flags = new List<string> { PixelFlags.Enabled, PixelFlags.LowRes, PixelFlags.Quantise };
                string name = "";
                for (int b = 0; b < 4; b++) if ((mask & (1 << b)) != 0) { flags.Add(optional[b]); name += optional[b].Substring(16) + " "; }
                Configure(flags);
                AssertOnPalette(Render(), m_Binder.profile, "stages [" + name + "]");
            }
            Assert.AreEqual(0, PixelLog.OrderViolations);
        }

        [Test]
        public void EachStage_ChangesTheFrame_WhenToggled()
        {
            Frame all = Render();
            foreach (string flag in new[] { PixelFlags.Haze, PixelFlags.Dither, PixelFlags.Quantise, PixelFlags.LowRes })
            {
                var flags = new List<string>(M0); flags.Remove(flag); Configure(flags);
                Frame f = Render();
                if (flag == PixelFlags.LowRes) { Assert.AreEqual(OutW, f.W, "without the low-res stage the internal image is at output resolution"); continue; }
                CollectionAssert.AreNotEqual(all.Internal, f.Internal, flag + " off changes the image");
            }
            foreach (string flag in new[] { PixelFlags.Outline, PixelFlags.Supersample })
            {
                Configure(new List<string>(M0) { flag });
                CollectionAssert.AreNotEqual(all.Internal, Render().Internal, flag + " on changes the image");
            }
        }

        [Test]
        public void SixthProfile_FromAJsonFixture_NeedsNoCodeOrShaderChange()
        {
            string json = File.ReadAllText("Assets/Game/Rendering/Pixel/Tests/UnityOnly/Fixtures/palette_set_sixth.json");
            var data = PaletteSetReader.Read(json);
            Assert.AreEqual(1, data.Count);
            var profile = PaletteProfileGenerator.CreateInMemory(data[0], File.ReadAllText(PaletteProfileGenerator.TuningPath)); m_Temp.Add(profile);
            Assert.AreEqual("fixture_moss", profile.profileId); Assert.AreEqual(11, profile.colours.Length); Assert.AreEqual(10, profile.accentIndex);
            m_Binder.profile = profile;
            Frame f = Render();
            AssertOnPalette(f, profile, "sixth profile");
            // M-003a on the capture with the engine-free CIEDE2000: every pixel within dE00 2.0 of the declared profile.
            var lab = ColourMath.ToLab(profile.ColourList()); int ok = 0;
            foreach (Color32 c in f.Output) { ColourMath.NearestIndex(lab, ColourMath.ToLab(new Rgb8(c.r, c.g, c.b)), out double de); if (de <= 2.0) ok++; }
            Assert.AreEqual(f.Output.Length, ok, "A_2 on the output is 1.0");
        }

        [Test]
        public void EditingOneColour_AndRebaking_ChangesTheCapture_AndStaysOnTheEditedPalette()
        {
            Frame before = Render();
            var edited = UnityEngine.Object.Instantiate(m_Binder.profile); edited.hideFlags = HideFlags.HideAndDontSave; m_Temp.Add(edited);
            int used = before.Internal[0].a;   // a colour the frame uses
            edited.colours[used] = new Color32(200, 60, 160, 255);
            edited.Bake();
            Assert.AreNotEqual(m_Binder.profile.paletteSha256, edited.paletteSha256);
            m_Binder.profile = edited;
            Frame after = Render();
            CollectionAssert.AreNotEqual(before.Internal, after.Internal);
            AssertOnPalette(after, edited, "edited profile");
            CollectionAssert.AreEqual(before.Depth, after.Depth, "nothing but colour changes");
        }

        [Test]
        public void GeneratedProfiles_MatchThePaletteSet_AndTheirLutIsAFreshBake()
        {
            var data = PaletteSetReader.Read(File.ReadAllText(PaletteProfileGenerator.PaletteSetPath));
            Assert.AreEqual(5, m_Catalog.profiles.Length);
            foreach (var d in data)
            {
                var p = m_Catalog.FindProfile(d.Id); Assert.IsNotNull(p, d.Id);
                CollectionAssert.AreEqual(d.Colours, p.ColourList(), d.Id + ": hex strings are authoritative");
                CollectionAssert.AreEqual(d.RampUniform, p.HazeRampList(), d.Id + ": haze ramp is ramp_uniform_L");
                Assert.AreEqual(d.Sha256, p.paletteSha256, d.Id); Assert.AreEqual(d.AccentIndex, p.accentIndex, d.Id);
                Assert.IsTrue(p.IsBaked, d.Id); CollectionAssert.AreEqual(PaletteLutBaker.Bake(p.ToLutInput()), p.lut, d.Id + ": stored LUT equals a fresh bake");
                Assert.AreEqual(p.lutSize, p.LutTexture.width); Assert.AreEqual(PaletteProfile.PaletteTextureWidth, p.PaletteTexture.width);
            }
        }

        [Test]
        public void Installer_KeepsTheFeatureListAndMapInStep_InTheDefaultOrder()
        {
            var world = (UniversalRendererData)AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(PixelPipelineInstaller.WorldRendererPath);
            CollectionAssert.AreEqual(PixelStageOrder.Default, PixelPipelineInstaller.ReadOrder(world));
            var so = new SerializedObject(world); var list = so.FindProperty("m_RendererFeatures"); var map = so.FindProperty("m_RendererFeatureMap");
            Assert.AreEqual(list.arraySize, map.arraySize);
            for (int i = 0; i < list.arraySize; i++)
            {
                var feature = list.GetArrayElementAtIndex(i).objectReferenceValue as PixelStageFeature; Assert.IsNotNull(feature);
                Assert.IsNotNull(feature.shader, feature.name);
                Assert.IsTrue(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long id)); Assert.AreEqual(id, map.GetArrayElementAtIndex(i).longValue);
            }
            Assert.AreSame(m_Catalog.pipeline, AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PixelPipelineInstaller.PipelinePath));
        }

        const string TempFolder = "Assets/Game/Rendering/Pixel/Tests/UnityOnly/TempInstall";

        [Test]
        public void Installer_RejectsIllegalOrders_AndWritesNothing()
        {
            var illegal = new[]
            {
                new[] { PixelStageId.Quantise, PixelStageId.Haze, PixelStageId.Store, PixelStageId.Present },
                new[] { PixelStageId.Haze, PixelStageId.Present, PixelStageId.Quantise, PixelStageId.Store },
                new[] { PixelStageId.Dither, PixelStageId.Downsample, PixelStageId.Quantise, PixelStageId.Store, PixelStageId.Present },
                new[] { PixelStageId.Haze, PixelStageId.Quantise },
            };
            foreach (var order in illegal)
                Assert.Throws<ArgumentException>(() => PixelPipelineInstaller.Install(order, TempFolder + "/P.asset", TempFolder + "/W.asset", TempFolder + "/R.asset", TempFolder + "/C.asset"));
            Assert.IsFalse(AssetDatabase.IsValidFolder(TempFolder), "a rejected order creates no assets");
        }

        [Test]
        public void LegalReorder_GivesTheIdenticalFrame_AndAnIllegalRuntimeOrderIsSkippedWithAWarning()
        {
            Frame reference = Render();
            try
            {
                var legal = new[] { PixelStageId.Downsample, PixelStageId.Outline, PixelStageId.Dither, PixelStageId.Haze, PixelStageId.Quantise, PixelStageId.Store, PixelStageId.Present };
                var catalog = PixelPipelineInstaller.Install(legal, TempFolder + "/P.asset", TempFolder + "/W.asset", TempFolder + "/R.asset", TempFolder + "/C.asset");
                AssetDatabase.SaveAssets();
                using (new PixelPipelineScope(catalog.pipeline))
                    CollectionAssert.AreEqual(reference.Internal, Render().Internal, "a legal order with Outline off renders the same frame");

                // Bypass the installer's check to prove the run-time guard: Quantise listed before Haze.
                var world = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(TempFolder + "/W.asset");
                var so = new SerializedObject(world); var list = so.FindProperty("m_RendererFeatures"); var map = so.FindProperty("m_RendererFeatureMap");
                int haze = -1, quantise = -1;
                for (int i = 0; i < list.arraySize; i++)
                {
                    var f = (PixelStageFeature)list.GetArrayElementAtIndex(i).objectReferenceValue;
                    if (f.Stage == PixelStageId.Haze) haze = i; if (f.Stage == PixelStageId.Quantise) quantise = i;
                }
                list.MoveArrayElement(quantise, haze); map.MoveArrayElement(quantise, haze);
                so.ApplyModifiedPropertiesWithoutUndo(); world.SetDirty(); AssetDatabase.SaveAssets();
                PixelLog.Reset();
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("stage Haze skipped: illegal stage order"));
                using (new PixelPipelineScope(catalog.pipeline))
                {
                    Frame f = Render();
                    AssertOnPalette(f, m_Binder.profile, "illegal order");
                    Assert.Greater(PixelLog.OrderViolations, 0, "the late Haze stage was skipped, not run on quantised colours");
                }
            }
            finally { AssetDatabase.DeleteAsset(TempFolder); }
        }
    }
}
