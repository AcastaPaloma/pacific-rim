using System;
using System.Reflection;
using UnityEngine;

namespace Game.Rendering.Pixel
{
    /// <summary>
    /// Applies Config/Rendering/PaletteProfileTuning.json to a profile: "defaults" first, then "profiles.&lt;id&gt;".
    /// Keys are PaletteProfile field names (float, int or string), so a new tuning value needs no code.
    /// </summary>
    public static class PaletteProfileTuning
    {
        /// <summary>Keys the shipped "defaults" object is expected to carry; the PaletteProfile code defaults duplicate them (DEF-T004-arch-5).</summary>
        static readonly string[] ExpectedDefaults =
        {
            "hazeStartM", "hazeFullM", "hazeCurveGamma", "hazeBands", "hazeSeamTexels", "hazeSeamWidth", "hazeFarStartM", "hazeFarFullM", "hazeFarPower", "airlightValue", "skyHorizonValue", "skyZenithValue",
            "skyZenithElevationDeg", "valueScale", "valueBias", "valueGamma", "accentChromaStart", "accentChromaFull", "ditherSpread",
            "ditherGradientMin", "lutChromaWeight", "ladderMinStepL", "ladderHueTolerance", "ladderTopL", "pairPenalty", "lutCandidates", "lutSize",
        };

        public static void Apply(PaletteProfile profile, string tuningJson)
        {
            if (string.IsNullOrEmpty(tuningJson)) return;
            var root = MiniJson.Parse(tuningJson) as MiniJson.Obj ?? throw new FormatException("palette tuning: root is not an object");
            if (!root.Has("schema") || (int)root.Num("schema") != 1) throw new FormatException("palette tuning: schema must be 1");
            if (root.Has("defaults"))
            {
                var defaults = root.Object("defaults");
                foreach (string key in ExpectedDefaults)
                    if (!defaults.ContainsKey(key)) Debug.LogWarning("PaletteProfileTuning.json: defaults has no '" + key + "'; PaletteProfile uses its code default");
                ApplyObject(profile, defaults);
            }
            else Debug.LogWarning("PaletteProfileTuning.json: no 'defaults' object; PaletteProfile uses its code defaults");
            if (root.Has("profiles") && root.Object("profiles").Has(profile.profileId)) ApplyObject(profile, root.Object("profiles").Object(profile.profileId));
        }

        static void ApplyObject(PaletteProfile profile, MiniJson.Obj values)
        {
            foreach (string key in values.Order)
            {
                FieldInfo field = typeof(PaletteProfile).GetField(key, BindingFlags.Public | BindingFlags.Instance);
                if (field == null) throw new FormatException("palette tuning: PaletteProfile has no field '" + key + "'");
                object v = values[key];
                if (field.FieldType == typeof(float)) field.SetValue(profile, (float)(double)v);
                else if (field.FieldType == typeof(int)) field.SetValue(profile, (int)(double)v);
                else if (field.FieldType == typeof(string)) field.SetValue(profile, (string)v);
                else throw new FormatException("palette tuning: field '" + key + "' cannot be set from JSON");
            }
        }
    }
}
