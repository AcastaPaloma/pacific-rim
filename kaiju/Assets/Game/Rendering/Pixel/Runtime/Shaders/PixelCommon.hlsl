// Shared by every pixel-pipeline stage shader (Rendering.Pixel, T-004). All inputs are bound per pass through a
// MaterialPropertyBlock: no stage reads a global set by another stage, so a stage that is switched off cannot leave
// stale state behind. Pixel coordinates have their origin at the bottom-left (texcoord space of Blit.hlsl).
#ifndef GAME_PIXEL_COMMON_INCLUDED
#define GAME_PIXEL_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

TEXTURE2D_X_FLOAT(_PixelDepthTex);
TEXTURE2D(_PixelThresholdTex);

float4 _PixelTargetSize;   // xy = size of this pass's target in pixels, z = source pixels per target pixel, w = depth pixels per target pixel
float4 _PixelThresholdInfo;// x = 1 when a dither threshold texture is bound, y = target pixels per threshold texel
float4 _PixelCamPos;       // world position of the rendered camera
float4 _PixelCamRight;     // world right, up and forward axes of the rendered camera
float4 _PixelCamUp;
float4 _PixelCamForward;
float4 _PixelTanHalf;      // xy = tan of half the horizontal / vertical field of view of the whole target (guard band included)

float3 PixelSrgbToLinear(float3 c)
{
    float3 lo = c / 12.92;
    float3 hi = pow(max((c + 0.055) / 1.055, 0.0), 2.4);
    return lerp(hi, lo, step(c, 0.04045));
}

float3 PixelLinearToSrgb(float3 c)
{
    float3 lo = c * 12.92;
    float3 hi = 1.055 * pow(max(c, 0.0), 1.0 / 2.4) - 0.055;
    return lerp(hi, lo, step(c, 0.0031308));
}

int2 PixelCoord(float2 uv)
{
    return int2(floor(uv * _PixelTargetSize.xy));
}

float4 PixelLoadSource(int2 pix)
{
    return LOAD_TEXTURE2D_X(_BlitTexture, pix * int(_PixelTargetSize.z));
}

// Eye-space linear depth (m) along the camera forward axis; negative where there is no geometry (sky).
float PixelEyeDepth(int2 pix)
{
    float raw = LOAD_TEXTURE2D_X(_PixelDepthTex, pix * int(_PixelTargetSize.w)).r;
#if UNITY_REVERSED_Z
    if (raw <= 0.0) return -1.0;
#else
    if (raw >= 1.0) return -1.0;
#endif
    return LinearEyeDepth(raw, _ZBufferParams);
}

// View-space ray through the centre of a target pixel, z = 1.
float3 PixelViewRay(int2 pix)
{
    float2 ndc = (float2(pix) + 0.5) / _PixelTargetSize.xy * 2.0 - 1.0;
    return float3(ndc * _PixelTanHalf.xy, 1.0);
}

float3 PixelWorldRay(float3 viewRay)
{
    return _PixelCamRight.xyz * viewRay.x + _PixelCamUp.xyz * viewRay.y + _PixelCamForward.xyz * viewRay.z;
}

// Dither threshold in (0, 1) for a target pixel; 0.5 when the Dither stage is off (nearest colour, hard seams).
float PixelThreshold(int2 pix)
{
    if (_PixelThresholdInfo.x < 0.5) return 0.5;
    return LOAD_TEXTURE2D(_PixelThresholdTex, pix / int(_PixelThresholdInfo.y)).r;
}

// 1 where the scene value changes locally (a shading gradient, the sky), 0 on flat faces: only gradients are dithered
// between palette entries; a flat face takes its nearest entry. 0 when the Dither stage is off.
float PixelGradientMask(int2 pix)
{
    if (_PixelThresholdInfo.x < 0.5) return 0.0;
    return LOAD_TEXTURE2D(_PixelThresholdTex, pix / int(_PixelThresholdInfo.y)).g;
}

#endif
