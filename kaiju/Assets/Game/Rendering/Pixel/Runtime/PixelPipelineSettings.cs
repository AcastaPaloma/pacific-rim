using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Game.Rendering.Pixel
{
    /// <summary>Pipeline tuning values, read from Assets/Game/Config/Rendering/PixelPipeline.json (schema 1).</summary>
    [Serializable]
    public sealed class PixelPipelineSettings
    {
        public int schema = PixelContractVersions.PipelineSettings;
        // Internal resolution (reconciled spec C2): k = clamp(round(H / targetLines), minScale, maxScale).
        public int targetLines = 270;
        public int minScale = 4;
        public int maxScale = 6;
        public int guardTexels = 1;
        public int supersample = 2;
        // Dither
        public string ditherMask = "bayer4";
        public string ditherAnchor = "hybrid";
        public float hybridFarStartM = 1500f;
        public float surfaceCellTexels = 1f;
        // Outline (optional stage)
        public float outlineDepthThreshold = 0.08f;
        public float outlineMaxDistanceM = 400f;
        public float outlineStrength = 0.6f;
        public string defaultProfile = "slate_teal";

        public static PixelPipelineSettings FromJson(string json)
        {
            var s = JsonUtility.FromJson<PixelPipelineSettings>(json);
            if (s == null) throw new FormatException("PixelPipeline.json: empty");
            if (s.schema != PixelContractVersions.PipelineSettings) throw new FormatException("PixelPipeline.json: schema " + s.schema + " is not " + PixelContractVersions.PipelineSettings);
            if (s.targetLines < 1 || s.minScale < 1 || s.maxScale < s.minScale || s.guardTexels < 0 || s.supersample < 1) throw new FormatException("PixelPipeline.json: value out of range");
            ParseAnchor(s.ditherAnchor);
            DitherMasks.Create(s.ditherMask, out _);
            return s;
        }

        public PixelPipelineSettings Clone() => (PixelPipelineSettings)MemberwiseClone();

        public static PixelDitherAnchor ParseAnchor(string name)
        {
            switch (name)
            {
                case "screen": return PixelDitherAnchor.Screen;
                case "direction": return PixelDitherAnchor.Direction;
                case "hybrid": return PixelDitherAnchor.Hybrid;
                default: throw new FormatException("unknown dither anchor '" + name + "' (screen, direction, hybrid)");
            }
        }

        public PixelDitherAnchor Anchor => ParseAnchor(ditherAnchor);
    }

    /// <summary>
    /// A named pipeline configuration (for example the M0 style frame): the list of feature flags it switches on.
    /// Flags ship false in Config/FeatureFlags (MODULARITY rule 5); a configuration enables stages by config, not code.
    /// File schema: { "schema": 1, "name": "...", "enable": ["rendering.pixel.haze", ...] }.
    /// </summary>
    [Serializable]
    public sealed class PixelConfiguration
    {
        public int schema = 1;
        public string name = "";
        public string[] enable = Array.Empty<string>();

        public static PixelConfiguration FromJson(string json)
        {
            var c = JsonUtility.FromJson<PixelConfiguration>(json);
            if (c == null || c.schema != 1) throw new FormatException("pixel configuration: schema must be 1");
            return c;
        }
    }

    /// <summary>Effective on/off state of the pixel flags: the session's feature flags, plus a configuration's enables.</summary>
    public sealed class PixelFlagState
    {
        readonly HashSet<string> m_On = new HashSet<string>(StringComparer.Ordinal);

        public static PixelFlagState From(IFeatureFlags flags, PixelConfiguration configuration)
        {
            var state = new PixelFlagState();
            if (flags != null)
                for (int i = 0; i < flags.Count; i++) if (flags.ValueAt(i)) state.m_On.Add(flags.NameAt(i));
            if (configuration != null)
                foreach (string name in configuration.enable) state.m_On.Add(name);
            return state;
        }

        public static PixelFlagState FromNames(IEnumerable<string> on)
        {
            var state = new PixelFlagState();
            foreach (string name in on) state.m_On.Add(name);
            return state;
        }

        public bool IsOn(string flag) => m_On.Contains(flag);
        public PixelFlagState With(string flag, bool on) { var s = FromNames(m_On); if (on) s.m_On.Add(flag); else s.m_On.Remove(flag); return s; }
        public IEnumerable<string> OnFlags => m_On;

        public bool StageEnabled(PixelStageId stage)
        {
            if (!IsOn(PixelFlags.Enabled)) return false;
            string flag = PixelFlags.ForStage(stage);
            return flag == null || IsOn(flag);
        }
    }
}
