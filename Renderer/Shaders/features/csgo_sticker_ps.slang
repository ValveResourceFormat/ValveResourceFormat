#version 460

#ifndef VRF_CSGO_STICKER_PS_INCLUDE
#define VRF_CSGO_STICKER_PS_INCLUDE

// A sticker drawn over the surface in its [0, 1] uv square, with optional wear, holographic and glitter layers.
// Expects the base albedo, normal map, roughness, metalness and ambient occlusion to be resolved.

#include "automatic_pbr_color_fitting_ps.slang"

uniform sampler2D g_tSticker0; // SrgbRead(true)
uniform sampler2D g_tNormalRoughnessSticker0;
uniform sampler2D g_tStickerScratches;
uniform sampler2D g_tSfxMaskSticker0;
uniform sampler2D g_tHoloSpectrumSticker0; // SrgbRead(true)
uniform sampler2D g_tGlitterNormalSticker0;

uniform bool g_bEnableSticker0 = true;
uniform bool g_bAutomaticPBRColorFittingSticker0 = true;
uniform bool g_bLegacyTintMultiplySticker0 = false;
uniform bool g_bMetallicSticker0 = false;
uniform bool g_bSelfIllumSticker0 = false;
uniform bool g_bPaperBackingSticker0 = false;
uniform bool g_bPreserveRoughnessSticker0 = false;
uniform bool g_bHolographicSticker0 = false;
uniform bool g_bClampSpectrumVSticker0 = true;
uniform bool g_bGlitterSticker0 = false;
uniform vec3 g_vColorTintSticker0 = vec3(1.0); // SrgbRead(true)
uniform float g_flTintSaturateSticker0 = 1.0;
uniform float g_flColorBoostSticker0 = 1.0;
uniform float g_flSfxColorBoostSticker0 = 1.0;
uniform float g_flGlitterScaleSticker0 = 1.0;
uniform float g_flSticker0Wear = 0.0;
uniform vec2 g_vWearBiasSticker0 = vec2(1.0);
uniform float g_fWearScratchesSticker0 = 1.0;

const vec3 StickerHoloAlbedoLevels = vec3(0.05, 0.6215, 0.85);

vec3 FitStickerAlbedo(vec3 color, vec3 hueSource, float metallic)
{
    return FitAlbedoToMaterialLevels(color, hueSource, metallic, g_flColorBoostSticker0);
}

// Glints of a glitter flake normal, as the view reflects off it
vec3 GetGlitterSparkle(vec3 flakeNormal, MaterialProperties_t mat)
{
    vec3 flakeNormalWs = normalize(mat.Tangent * flakeNormal.x + mat.Bitangent * flakeNormal.y + mat.GeometricNormal * flakeNormal.z);
    vec3 wave = sin(reflect(mat.ViewDir, flakeNormalWs) * 12.0);

    vec3 peaks = max(vec3(0.0), wave - vec3(0.99)) * 100.0001;
    float troughs = pow(GetLuma(saturate(-wave)), 4.0);
    float shoulders = GetLuma(saturate(vec3(0.15) - wave)) * 0.25;

    return vec3(troughs + shoulders) + (peaks + pow(peaks.yzx, vec3(4.0))) * 3.0;
}

// Flake normal in tangent space, with y pointing along the bitangent
vec3 DecodeGlitterNormal(vec4 glitterTexel)
{
    vec3 flakeNormal = DecodeHemiOctahedronNormal(glitterTexel.rg);
    flakeNormal.y = -flakeNormal.y;
    return flakeNormal;
}

// Two layers of glitter flakes. Returns the glitter color, lit like the diffuse albedo,
// tilts the sticker normal and raises its metalness.
vec3 ApplyStickerGlitter(vec2 stickerCoord, vec3 color, float glitterAmount, MaterialProperties_t mat, inout vec3 normal, inout float metallic)
{
    float scale = 2.5 * g_flGlitterScaleSticker0;
    vec2 flakeCoord = stickerCoord * scale;
    vec2 offsetFlakeCoord = (vec2(0.5) + stickerCoord) * scale;
    vec2 flakeCoordDerivative = max(dFdx(flakeCoord), dFdy(flakeCoord));

    // Filtered read of the first layer
    vec4 flakeTexel = texture(g_tGlitterNormalSticker0, flakeCoord);
    vec4 offsetFlakeTexel = textureLod(g_tGlitterNormalSticker0, offsetFlakeCoord, 0.0);

    vec3 flakeNormal = DecodeGlitterNormal(flakeTexel);
    flakeNormal *= flakeNormal.z;
    vec3 offsetFlakeNormal = DecodeGlitterNormal(offsetFlakeTexel);

    // Fades out where the flakes get smaller than a pixel
    float tilt = 0.04 * glitterAmount * saturate(1.0 - min(flakeCoordDerivative.x, flakeCoordDerivative.y) * 40.0);
    normal.xy += flakeNormal.xy * tilt;
    normal.xy += offsetFlakeNormal.xy * tilt * offsetFlakeTexel.a;

    vec3 sparkle = max(GetGlitterSparkle(flakeNormal, mat) * flakeTexel.a, GetGlitterSparkle(offsetFlakeNormal, mat) * offsetFlakeTexel.a);

    vec3 glitterColor = color;
    if (g_bAutomaticPBRColorFittingSticker0)
    {
        glitterColor = FitAlbedoToLevels(color, color, PbrAlbedoLevels, PbrAlbedoLevels.x, 1.0, 1.0);
    }

    metallic = max(metallic, glitterAmount * 0.5);
    return sparkle * glitterColor * g_flSfxColorBoostSticker0 * glitterAmount;
}

// A view dependent spectrum, fitted into the albedo levels of a metal-free material
vec3 GetHolographicAlbedo(vec3 holoMask, MaterialProperties_t mat)
{
    vec2 spectrumCoord = vec2(holoMask.y + dot(mat.ViewDir, mat.GeometricNormal) + dot(mat.ViewDir, g_vSunDirection.xyz), holoMask.z);

    if (g_bClampSpectrumVSticker0)
    {
        float halfTexel = 0.5 / float(textureSize(g_tHoloSpectrumSticker0, 0).y);
        spectrumCoord.y = clamp(spectrumCoord.y, halfTexel, 1.0 - halfTexel);
    }

    const float lodBias = -1.0;
    vec3 spectrum = texture(g_tHoloSpectrumSticker0, spectrumCoord, lodBias).rgb;
    float boost = g_flSfxColorBoostSticker0;

    return FitAlbedoToLevels(spectrum, spectrum, StickerHoloAlbedoLevels, StickerHoloAlbedoLevels.x, boost, boost);
}

vec3 GetStickerAlbedo(vec3 color, float metallic)
{
    float desaturation = 1.0 - g_flTintSaturateSticker0;

    if (g_bAutomaticPBRColorFittingSticker0)
    {
        vec3 tint = mix(vec3(1.0), g_vColorTintSticker0, metallic);
        float tintDesaturation = desaturation * metallic;

        if (g_bLegacyTintMultiplySticker0)
        {
            vec3 desaturated = mix(color, vec3(GetLuma(color)), tintDesaturation);
            vec3 tinted = saturate(desaturated * tint);
            return FitStickerAlbedo(tinted, tinted, metallic);
        }

        vec3 tintedHue = mix(tint * color, tint, tintDesaturation);
        return FitStickerAlbedo(color, tintedHue, metallic);
    }

    if (g_bMetallicSticker0)
    {
        vec3 tinted = mix(color, vec3(GetLuma(color)), desaturation) * g_vColorTintSticker0;

        if (g_bLegacyTintMultiplySticker0)
        {
            tinted *= g_flColorBoostSticker0;
        }

        return tinted;
    }

    return color;
}

// Returns the glitter color to add to the diffuse color
vec3 ApplySticker(inout MaterialProperties_t mat, vec2 texCoord)
{
    if (!g_bEnableSticker0)
    {
        return vec3(0.0);
    }

    vec2 stickerCoord = saturate(texCoord);
    bool outsideSticker = stickerCoord != texCoord;

    const float lodBias = -1.0;
    vec4 sticker = texture(g_tSticker0, stickerCoord, lodBias);
    vec4 normalRoughness = texture(g_tNormalRoughnessSticker0, stickerCoord, lodBias);

    vec2 texelSize = 1.0 / vec2(textureSize(g_tSticker0, 0));
    float stickerLod = textureQueryLod(g_tSticker0, clamp(stickerCoord, texelSize, 1.0 - texelSize)).x;

    float alpha = saturate(sticker.a * 12.75);

    if (outsideSticker || alpha <= 0.0)
    {
        return vec3(0.0);
    }

    // todo: up close the edge alpha is also widened by a view dependent offset sample
    float nearAlpha = (stickerLod < 4.0) ? alpha : 1.0;

    // The base normal and occlusion are blurred under the sticker
    float baseLod = max(stickerLod, 3.0);
    vec3 baseNormalBlurred = DecodeHemiOctahedronNormal(textureLod(g_tNormal, texCoord, baseLod).rg);
    float occlusionBlurred = textureLod(g_tAmbientOcclusion, texCoord, baseLod).r;

    vec3 stickerNormal = DecodeHemiOctahedronNormal(normalRoughness.rg);
    vec3 normal = normalize(baseNormalBlurred + stickerNormal * alpha * 2.0);
    float occlusion = min(pow(occlusionBlurred, 0.75), nearAlpha * 0.5 + 0.5);

    float metallic = g_bMetallicSticker0 ? 1.0 : 0.0;
    vec3 glitter = vec3(0.0);

    // A single filtered read of the mask
    vec4 sfxMask = vec4(0.0);
    if (g_bGlitterSticker0 || g_bHolographicSticker0)
    {
        sfxMask = texture(g_tSfxMaskSticker0, stickerCoord, lodBias);
    }

    float glitterAmount = 1.0 - sfxMask.a;
    if (g_bGlitterSticker0 && glitterAmount != 0.0)
    {
        glitter = ApplyStickerGlitter(stickerCoord, sticker.rgb, glitterAmount, mat, normal, metallic);
    }

    vec3 albedo = GetStickerAlbedo(sticker.rgb, metallic);
    float roughness = normalRoughness.b;

    vec3 illumColor = vec3(0.0);
    if (g_bSelfIllumSticker0)
    {
        illumColor = albedo * normalRoughness.a * 2.0;
    }

    float holoMask = sfxMask.r;
    if (g_bHolographicSticker0 && holoMask > 0.0)
    {
        albedo = mix(albedo, GetHolographicAlbedo(sfxMask.rgb, mat), holoMask);
        metallic *= 1.0 - holoMask;
    }

    float wear = g_flSticker0Wear;
    float wearAmount = saturate(wear);

    if (wear > 0.0)
    {
        float scratches = 1.0 - min(g_fWearScratchesSticker0, texture(g_tStickerScratches, stickerCoord, lodBias).r);
        scratches = mix(scratches, scratches * 0.5, wear);

        float erosionCurve = g_vWearBiasSticker0.x * g_vWearBiasSticker0.x;
        float erosion = saturate(wearAmount - pow(saturate((sticker.a - 0.0784) * 1.0851), erosionCurve));

        float cutoffBias = 0.5;
        if (g_bPaperBackingSticker0)
        {
            float backingThreshold = saturate(erosion * 2.0) + g_vWearBiasSticker0.y * wearAmount;
            vec3 backingColor = vec3(mix(0.7, 0.2 + 0.4 * scratches, wearAmount));
            albedo = mix(backingColor, albedo, smoothstep(backingThreshold, backingThreshold + 0.1, scratches));
        }
        else
        {
            float biasScale = (g_vWearBiasSticker0.y > 0.0) ? 0.5 : 0.25;
            cutoffBias = (g_vWearBiasSticker0.y * biasScale + 0.5) * 0.5;
        }

        if (!g_bMetallicSticker0 && !g_bPreserveRoughnessSticker0)
        {
            float scratched = step(scratches, smoothstep(0.0, 0.35, wearAmount));
            roughness = mix(roughness, 0.8, scratched);
        }

        float cutoff = saturate(erosion * (1.0 + cutoffBias) - cutoffBias);
        albedo = mix(albedo, albedo * scratches, wearAmount * 0.3);
        alpha *= smoothstep(cutoff, cutoff + 0.1, scratches);
    }

    // Translucent and worn edges tilt the normal by the offset from the sticker center
    normal.xy = mix(stickerCoord - vec2(0.5), normal.xy, alpha);

    float coverage = nearAlpha * alpha;

    mat.Albedo = mix(mat.Albedo, saturate(albedo), coverage);
    mat.NormalMap = normalize(mix(mat.NormalMap, normal, coverage));
    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);
    mat.RoughnessTex = mix(mat.RoughnessTex, vec2(roughness), coverage);
    mat.Metalness = mix(mat.Metalness, metallic, coverage);
    mat.AmbientOcclusion = mix(mat.AmbientOcclusion, occlusion, alpha);
    mat.IllumColor += illumColor * coverage;

    return glitter * coverage;
}

#endif
