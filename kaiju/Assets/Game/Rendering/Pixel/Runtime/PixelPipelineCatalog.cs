using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Game.Rendering.Pixel
{
    /// <summary>
    /// Runtime lookup of the generated pixel assets (created by PixelPipelineInstaller): palette profiles by id, the
    /// dedicated URP asset and the settings file. Referenced by PixelCameraBinder so players need no asset database.
    /// </summary>
    public sealed class PixelPipelineCatalog : ScriptableObject
    {
        public UniversalRenderPipelineAsset pipeline;
        public int worldRendererIndex = 0;
        public int presentRendererIndex = 1;
        public TextAsset settingsJson;
        public PaletteProfile[] profiles = Array.Empty<PaletteProfile>();

        public PaletteProfile FindProfile(string id)
        {
            foreach (var p in profiles) if (p != null && p.profileId == id) return p;
            return null;
        }

        public PixelPipelineSettings LoadSettings() => PixelPipelineSettings.FromJson(settingsJson.text);
    }
}
