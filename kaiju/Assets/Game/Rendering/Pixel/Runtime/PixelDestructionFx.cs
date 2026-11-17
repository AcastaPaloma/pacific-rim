using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Rendering.Pixel
{
    /// <summary>
    /// T-207 destruction cue: a ~1.5 s collapse flash plus dust column and debris, built from axis-aligned cubes that are ordinary world geometry, so the pixel
    /// pipeline draws them into the low-res frame (integer grid, no sub-pixel placement) and the quantise stage maps them onto the active palette. Colours come from
    /// the profile's own haze ramp (dust, flash) and accent colour (embers); nothing outside the profile. Colour steps by age (no alpha), as pixel art does.
    /// Behind rendering.pixel.destruction_fx (default off). Owns no gameplay state; call <see cref="Burst"/> when a building has been removed.
    /// </summary>
    public sealed class PixelDestructionFx : MonoBehaviour
    {
        public const float LifeS = 1.5f;
        const int DustCount = 56, DebrisCount = 26;

        sealed class Part { public Transform T; public Renderer R; public Vector3 Pos, Vel; public float Life, S0, S1, Ground; public bool Debris, Ember; }
        sealed class Burst_ { public float Age; public readonly List<Part> Parts = new List<Part>(); public Part Flash; public Vector3 FlashMin, FlashMax; }

        Material _flash, _light, _mid, _dark, _ember;
        Camera _cam; float _lowResHeight = 270f;
        readonly List<Burst_> _bursts = new List<Burst_>();
        public int ActiveBursts => _bursts.Count;
        public int Spawned { get; private set; }

        /// <param name="baseMaterial">Any URP Lit material (its shader is reused; a new instance per colour is created).</param>
        public static PixelDestructionFx Create(Material baseMaterial, PaletteProfile profile, Camera cam, float lowResHeight)
        {
            var go = new GameObject("PixelDestructionFx");
            var fx = go.AddComponent<PixelDestructionFx>();
            fx._cam = cam; fx._lowResHeight = Mathf.Max(64f, lowResHeight);
            Color lightest = new Color(0.95f, 0.93f, 0.88f), light = new Color(0.8f, 0.78f, 0.74f), mid = new Color(0.55f, 0.52f, 0.5f), dark = new Color(0.25f, 0.23f, 0.24f), ember = new Color(0.9f, 0.45f, 0.1f);
            var r = profile != null ? profile.hazeRamp : null;
            if (r != null && r.Length >= 5)
            {
                // T-197b: every cue colour is a ramp entry chosen for luminance contrast against the typical (mid-ramp) background; the materials are emissive
                // (Mat), so lighting adds nothing and the entry reaches haze + quantise as is.
                int n = r.Length; var lum = new float[n];
                for (int i = 0; i < n; i++) lum[i] = DestructionFxLook.Luma(r[i].r / 255f, r[i].g / 255f, r[i].b / 255f);
                float bg = lum[n / 2];
                Color At(int i) => (Color)r[i];
                lightest = At(DestructionFxLook.PickContrasting(lum, n - 1, bg, DestructionFxLook.MinContrast));
                light = At(DestructionFxLook.PickContrasting(lum, n - 2, bg, DestructionFxLook.MinContrast));
                mid = At(Mathf.Max(2, n / 2));
                dark = At(DestructionFxLook.PickContrasting(lum, Mathf.Min(n - 3, 1), bg, DestructionFxLook.MinContrast));
            }
            if (profile != null && profile.accentIndex >= 0 && profile.accentIndex < profile.colours.Length) ember = profile.colours[profile.accentIndex];
            fx._flash = Mat(baseMaterial, lightest); fx._light = Mat(baseMaterial, light); fx._mid = Mat(baseMaterial, mid); fx._dark = Mat(baseMaterial, dark); fx._ember = Mat(baseMaterial, ember);
            return fx;
        }

        /// <summary>Emissive material: base colour black (no lit contribution), emission = the ramp colour, so the colour is independent of light angle / shadow / exposure.</summary>
        static Material Mat(Material b, Color c)
        {
            c.a = 1f; var m = new Material(b.shader); m.color = Color.black;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.black);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights", 0f);
            if (m.HasProperty("_EnvironmentReflections")) m.SetFloat("_EnvironmentReflections", 0f);
            m.EnableKeyword("_EMISSION"); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c * DestructionFxLook.EmissionGain(DestructionFxLook.PhaseLightest));
            return m;
        }

        Part NewPart(Material m, Vector3 pos, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = "FxPart";
            var c = go.GetComponent<Collider>(); if (c != null) Destroy(c);
            var r = go.GetComponent<Renderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            go.transform.SetParent(transform, false); go.transform.position = pos; go.transform.localScale = new Vector3(size, size, size);
            return new Part { T = go.transform, R = r, Pos = pos };
        }

        /// <summary>Spawns one burst over the world-space bounds of a removed building. groundY = terrain height under it.</summary>
        public void Burst(Vector3 mn, Vector3 mx, float groundY, int seed)
        {
            var rnd = new System.Random(seed); float W = mx.x - mn.x, D = mx.z - mn.z, H = Mathf.Max(6f, mx.y - Mathf.Min(mn.y, groundY)), span = Mathf.Max(8f, Mathf.Max(W, D));
            var ctr = new Vector3((mn.x + mx.x) * 0.5f, groundY, (mn.z + mx.z) * 0.5f);
            float dist = _cam != null ? Mathf.Max(20f, (_cam.transform.position - ctr).magnitude) : 150f;
            float tanHalf = _cam != null ? Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) : 0.47f;
            float px = 2f * dist * tanHalf / _lowResHeight;   // world size of one low-res pixel at the building
            var b = new Burst_();
            // collapse flash: the building's own bounds, one box, bright for ~0.1 s then stepping down
            float inflate = 1.06f; var fc = (mn + mx) * 0.5f; var fs = new Vector3(Mathf.Max(W, 3f * px) * inflate, Mathf.Max(mx.y - mn.y, 3f * px) * inflate, Mathf.Max(D, 3f * px) * inflate);
            var fl = NewPart(_flash, fc, 1f); fl.T.localScale = fs; b.Flash = fl; b.FlashMin = fc - fs * 0.5f; b.FlashMax = fc + fs * 0.5f;
            for (int i = 0; i < DustCount; i++)
            {
                float x = Mathf.Lerp(mn.x, mx.x, (float)rnd.NextDouble()), z = Mathf.Lerp(mn.z, mx.z, (float)rnd.NextDouble());
                float y = groundY + (float)rnd.NextDouble() * H * 0.45f;
                float s0 = Mathf.Max(3f * px, (0.12f + 0.16f * (float)rnd.NextDouble()) * Mathf.Max(span, H * 0.6f));
                var p = NewPart(_light, new Vector3(x, y, z), s0);
                var dir = new Vector3(x - ctr.x, 0f, z - ctr.z); if (dir.sqrMagnitude < 0.01f) dir = new Vector3((float)rnd.NextDouble() - 0.5f, 0f, (float)rnd.NextDouble() - 0.5f); dir.Normalize();
                p.Vel = dir * (0.15f + 0.5f * (float)rnd.NextDouble()) * span + Vector3.up * (0.2f + 0.5f * (float)rnd.NextDouble()) * H;
                p.Life = LifeS * (0.65f + 0.35f * (float)rnd.NextDouble()); p.S0 = s0; p.S1 = s0 * 1.9f; p.Ground = groundY; b.Parts.Add(p);
            }
            for (int i = 0; i < DebrisCount; i++)
            {
                float x = Mathf.Lerp(mn.x, mx.x, (float)rnd.NextDouble()), z = Mathf.Lerp(mn.z, mx.z, (float)rnd.NextDouble());
                float y = groundY + (0.3f + 0.7f * (float)rnd.NextDouble()) * H * 0.8f;
                float s0 = Mathf.Max(2f * px, (0.04f + 0.06f * (float)rnd.NextDouble()) * span);
                bool ember = i % 4 == 0; var p = NewPart(ember ? _ember : _dark, new Vector3(x, y, z), s0);
                var dir = new Vector3(x - ctr.x, 0f, z - ctr.z); if (dir.sqrMagnitude < 0.01f) dir = Vector3.right; dir.Normalize();
                p.Vel = dir * (0.3f + 0.9f * (float)rnd.NextDouble()) * span * 0.5f + Vector3.up * (0.15f + 0.45f * (float)rnd.NextDouble()) * H;
                p.Life = LifeS * (0.7f + 0.3f * (float)rnd.NextDouble()); p.S0 = s0; p.S1 = s0 * 0.7f; p.Ground = groundY; p.Debris = true; p.Ember = ember; b.Parts.Add(p);
            }
            _bursts.Add(b); Spawned++;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int bi = _bursts.Count - 1; bi >= 0; bi--)
            {
                var b = _bursts[bi]; b.Age += dt;
                if (b.Age >= LifeS) { foreach (var p in b.Parts) Destroy(p.T.gameObject); if (b.Flash != null) Destroy(b.Flash.T.gameObject); _bursts.RemoveAt(bi); continue; }
                if (b.Flash != null)
                {
                    int ph = DestructionFxLook.FlashPhase(b.Age); var m = ph == DestructionFxLook.PhaseLightest ? _flash : ph == DestructionFxLook.PhaseLight ? _light : ph == DestructionFxLook.PhaseMid ? _mid : null;
                    if (m == null) b.Flash.R.enabled = false; else { b.Flash.R.sharedMaterial = m; }
                }
                foreach (var p in b.Parts)
                {
                    if (p.Debris) p.Vel.y -= 9.8f * dt; else p.Vel *= Mathf.Max(0f, 1f - 1.1f * dt);
                    p.Pos += p.Vel * dt; if (p.Pos.y < p.Ground) { p.Pos.y = p.Ground; p.Vel = Vector3.zero; }
                    float u = b.Age / p.Life;
                    if (u >= 1f) { p.R.enabled = false; continue; }
                    float s = Mathf.Lerp(p.S0, p.S1, u); p.T.position = p.Pos; p.T.localScale = new Vector3(s, s, s);
                    if (p.Debris) p.R.sharedMaterial = p.Ember && u < 0.45f ? _ember : _dark;
                    else p.R.sharedMaterial = u < 0.22f ? _light : u < 0.6f ? _mid : _dark;
                }
            }
        }
    }
}
