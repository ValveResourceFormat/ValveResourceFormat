#version 460

#include "common/utils.slang"
#include "common/features.slang"
#include "environment_blend_features.slang"

#define IBL 1

in vec3 vFragPosition;

centroid in vec3 vCentroidNormalOut;
in vec3 vNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;
in vec4 vTexCoord; // UV1 in xy, UV2 in zw
centroid in vec4 vVertexColorOut;
in vec2 vColorBlendValues;

#if (WORLD_OVERLAY_OBJECT_SPACE == 1)
    in vec3 vPositionOs;
#endif

out vec4 outputColor;

// Layer 1
uniform sampler2D g_tColor1; // SrgbRead(true)
uniform sampler2D g_tNormalRoughness1;
uniform sampler2D g_tPacked1; // r=self illum, g=metalness

uniform mat4 g_mAlbedoColorCorrect1 = mat4(1.0);
uniform mat4 g_mTextureColorTint1 = mat4(1.0);

uniform bool g_bMaskColorTint1 = true;
uniform bool g_bMaskVertexColorTint1 = true;
uniform bool g_bModelTint1 = true;
uniform float g_fVertexColorStrength1 = 1.0;
uniform float g_fLayerNormalStrength1 = 1.0;
uniform float g_fLayerSelfIllumScale1 = 1.0;
uniform vec3 g_vLayerSelfIllumTint1 = vec3(1.0); // SrgbRead(true)
uniform vec2 g_vLayerRoughnessContrastBrightness1 = vec2(1.0, 0.0);
uniform vec2 g_vLayerTintMaskContrastBrightness1 = vec2(1.0, 0.0);
uniform vec2 g_vLayerMetalnessContrastBrightness1 = vec2(1.0, 0.0);

#if (F_LAYER1_BIPLANAR == 1)
    uniform float g_flBiPlanarTiling1 = 196.0;
#else
    uniform vec4 g_vScaleRotationTranslation1 = vec4(1.0, 0.0, 0.0, 0.0);
#endif

// Layer 2
uniform sampler2D g_tColor2; // SrgbRead(true)
uniform sampler2D g_tNormalRoughness2;
uniform sampler2D g_tPacked2;
uniform sampler2D g_tRevealMask2;

uniform mat4 g_mAlbedoColorCorrect2 = mat4(1.0);
uniform mat4 g_mTextureColorTint2 = mat4(1.0);

uniform bool g_bMaskColorTint2 = true;
uniform bool g_bMaskVertexColorTint2 = true;
uniform bool g_bModelTint2 = true;
uniform float g_fVertexColorStrength2 = 1.0;
uniform float g_fLayerNormalStrength2 = 1.0;
uniform float g_fLayerSelfIllumScale2 = 1.0;
uniform vec3 g_vLayerSelfIllumTint2 = vec3(1.0); // SrgbRead(true)
uniform vec2 g_vLayerRoughnessContrastBrightness2 = vec2(1.0, 0.0);
uniform vec2 g_vLayerTintMaskContrastBrightness2 = vec2(1.0, 0.0);
uniform vec2 g_vLayerMetalnessContrastBrightness2 = vec2(1.0, 0.0);

uniform float g_flRevealOffset2 = 0.0;
uniform float g_flRevealSoftness2 = 0.125;
uniform bool g_bRevealInvert2 = false;
uniform bool g_bRevealUseSecondaryUv2 = false;
uniform vec4 g_vRevealScaleRotationTranslation2 = vec4(1.0, 0.0, 0.0, 0.0);

uniform bool g_bBorderEffects2 = false;
uniform vec2 g_vBorderWidth2 = vec2(0.125);
uniform vec2 g_vBorderSoftness2 = vec2(0.5);
uniform vec2 g_vBorderLayerAmount2 = vec2(1.0);
uniform int g_nBorderBlendMode2 = 0; // 0="Multiply", 1="Mod2x", 2="Blend"
uniform vec4 g_vBorderTint2 = vec4(0.5, 0.5, 0.5, 1.0); // SrgbRead(true)
uniform float g_flBorderRoughnessStrength2 = 0.0;
uniform float g_flBorderRoughness2 = 0.5;

#if (F_LAYER2_BIPLANAR == 1)
    uniform float g_flBiPlanarTiling2 = 196.0;
#else
    uniform vec4 g_vScaleRotationTranslation2 = vec4(1.0, 0.0, 0.0, 0.0);
#endif

// Layer 3
#if (F_LAYER_COUNT == 1)
    uniform sampler2D g_tColor3; // SrgbRead(true)
    uniform sampler2D g_tNormalRoughness3;
    uniform sampler2D g_tPacked3;
    uniform sampler2D g_tRevealMask3;

    uniform mat4 g_mAlbedoColorCorrect3 = mat4(1.0);
    uniform mat4 g_mTextureColorTint3 = mat4(1.0);

    uniform bool g_bMaskColorTint3 = true;
    uniform bool g_bMaskVertexColorTint3 = true;
    uniform bool g_bModelTint3 = true;
    uniform float g_fVertexColorStrength3 = 1.0;
    uniform float g_fLayerNormalStrength3 = 1.0;
    uniform float g_fLayerSelfIllumScale3 = 1.0;
    uniform vec3 g_vLayerSelfIllumTint3 = vec3(1.0); // SrgbRead(true)
    uniform vec2 g_vLayerRoughnessContrastBrightness3 = vec2(1.0, 0.0);
    uniform vec2 g_vLayerTintMaskContrastBrightness3 = vec2(1.0, 0.0);
    uniform vec2 g_vLayerMetalnessContrastBrightness3 = vec2(1.0, 0.0);

    uniform float g_flRevealOffset3 = 0.0;
    uniform float g_flRevealSoftness3 = 0.125;
    uniform bool g_bRevealInvert3 = false;
    uniform bool g_bRevealUseSecondaryUv3 = false;
    uniform vec4 g_vRevealScaleRotationTranslation3 = vec4(1.0, 0.0, 0.0, 0.0);

    uniform bool g_bBorderEffects3 = false;
    uniform vec2 g_vBorderWidth3 = vec2(0.125);
    uniform vec2 g_vBorderSoftness3 = vec2(0.5);
    uniform vec3 g_vBorderLayerAmount3 = vec3(1.0);
    uniform int g_nBorderBlendMode3 = 0;
    uniform vec4 g_vBorderTint3 = vec4(0.5, 0.5, 0.5, 1.0); // SrgbRead(true)
    uniform float g_flBorderRoughnessStrength3 = 0.0;
    uniform float g_flBorderRoughness3 = 0.5;

    #if (LAYER3_BIPLANAR == 1)
        uniform float g_flBiPlanarTiling3 = 196.0;
    #else
        uniform vec4 g_vScaleRotationTranslation3 = vec4(1.0, 0.0, 0.0, 0.0);
    #endif
#endif

// UV overlay: one texture laid over every layer, plus a mask that pushes layer 2 in on
// top of the vertex paint.
#if (F_UV_OVERLAY == 1)
    uniform sampler2D g_tColorOverlay; // SrgbRead(true)
    uniform sampler2D g_tNormalRoughnessOverlay;
    uniform sampler2D g_tRevealOverlay;

    uniform vec4 g_vOverlayScaleRotationTranslation = vec4(1.0, 0.0, 0.0, 0.0);
    uniform bool g_bUseSecondaryUvForOverlay = true;
    uniform float g_flOverlayDarknessContrast = 1.0;
    uniform float g_flOverlayBrightnessContrast = 1.0;

    uniform int g_nOverlayTintMask1 = 0; // 0="Unmasked", 1="Masked", 2="Inverted"
    uniform int g_nOverlayTintMask2 = 0;
    uniform float g_flOverlayStrength1 = 1.0;
    uniform float g_flOverlayStrength2 = 1.0;
    uniform float g_flOverlayNormalStrength1 = 1.0;
    uniform float g_flOverlayNormalStrength2 = 1.0;
    uniform float g_flOverlayRoughnessStrength1 = 0.0;
    uniform float g_flOverlayRoughnessStrength2 = 0.0;

    uniform bool g_bRevealOverlayInvert2 = false;
    uniform float g_flRevealOverlayStrength2 = 1.0;

    #if (F_LAYER_COUNT == 1)
        uniform int g_nOverlayTintMask3 = 0;
        uniform float g_flOverlayStrength3 = 1.0;
        uniform float g_flOverlayNormalStrength3 = 1.0;
        uniform float g_flOverlayRoughnessStrength3 = 0.0;
    #endif
#endif

// World overlay: biplanar-projected colour modulation, shared by every layer.
#if (F_WORLD_OVERLAY > 0)
    uniform sampler2D g_tColorWorldOverlay; // SrgbRead(true)

    uniform int g_nWorldOverlayTintMask1 = 1; // 0="Unmasked", 1="Masked", 2="Inverted"
    uniform int g_nWorldOverlayTintMask2 = 1;
    #if (F_LAYER_COUNT == 1)
        uniform int g_nWorldOverlayTintMask3 = 1;
    #endif

    #if (F_WORLD_OVERLAY == 2)
        uniform sampler2D g_tColorWorldOverlay2; // SrgbRead(true)

        uniform vec2 g_vWorldOverlayTiling = vec2(512.0);
        uniform vec2 g_vWorldOverlayBrightnessContrast = vec2(1.0);
        uniform vec2 g_vWorldOverlayDarknessContrast = vec2(1.0);
        uniform vec2 g_vWorldOverlayStrengths1 = vec2(1.0);
        uniform vec2 g_vWorldOverlayStrengths2 = vec2(1.0);
        #if (F_LAYER_COUNT == 1)
            uniform vec2 g_vWorldOverlayStrengths3 = vec2(1.0);
        #endif
    #else
        uniform float g_flWorldOverlayTiling = 512.0;
        uniform float g_flWorldOverlayBrightnessContrast = 1.0;
        uniform float g_flWorldOverlayDarknessContrast = 1.0;
        uniform float g_flWorldOverlayStrength1 = 1.0;
        uniform float g_flWorldOverlayStrength2 = 1.0;
        #if (F_LAYER_COUNT == 1)
            uniform float g_flWorldOverlayStrength3 = 1.0;
        #endif
    #endif
#endif

#include "common/ViewConstants.slang"
#include "common/LightingConstants.slang"

#include "common/fullbright.slang"
#include "common/texturing.slang"
#include "common/fog.slang"
#include "common/deadlock.slang"

// Must be last
#include "common/lighting.slang"

// Every UV here is driven by a (scale, rotation in degrees, translate u, translate v) vector.
vec2 ApplyUvTransform(vec2 texCoord, vec4 scaleRotationTranslation)
{
    float scale = scaleRotationTranslation.x;
    float angle = radians(scaleRotationTranslation.y);
    vec2 rotation = vec2(sin(angle), cos(angle)) * scale;

    vec2 offset = texCoord + scaleRotationTranslation.zw / ((scale == 0.0) ? 1.0 : scale);
    return vec2(rotation.y * offset.x - rotation.x * offset.y, dot(rotation, offset));
}

// The frame a layer's normal map is resolved against. Layers on the interpolated UVs use
// the mesh frame; projected ones have to derive theirs from screen derivatives.
struct LayerFrame_t
{
    vec2 TexCoord;
    vec3 Tangent;
    vec3 Bitangent;
};

LayerFrame_t GetMeshFrame(vec2 texCoord)
{
    LayerFrame_t frame;
    frame.TexCoord = texCoord;
    frame.Tangent = vTangentOut;
    frame.Bitangent = vBitangentOut;
    return frame;
}

// Mikkelsen's screen space tangent frame, for UVs that aren't the mesh's own.
LayerFrame_t GetDerivedFrame(vec2 texCoord, vec3 normal)
{
    vec3 positionDdx = dFdxFine(vFragPosition);
    vec3 positionDdy = dFdyFine(vFragPosition);

    vec3 sigmaX = positionDdx - normal * dot(positionDdx, normal);
    vec3 sigmaY = positionDdy - normal * dot(positionDdy, normal);
    float handedness = (dot(positionDdy, cross(normal, positionDdx)) < 0.0) ? -1.0 : 1.0;

    vec2 texCoordDdx = dFdxFine(texCoord);
    vec2 texCoordDdy = dFdyFine(texCoord);

    float area = dot(texCoordDdx, vec2(texCoordDdy.y, -texCoordDdy.x));
    float winding = (area < 0.0) ? -1.0 : 1.0;

    vec2 projection = vec2(texCoordDdy.y, -texCoordDdx.y) * winding;
    vec3 tangent = sigmaX * projection.x + sigmaY * projection.y;

    if (abs(area) > 0.0)
    {
        tangent = normalize(tangent);
    }

    LayerFrame_t frame;
    frame.TexCoord = texCoord;
    frame.Tangent = tangent;
    frame.Bitangent = cross(normal, tangent) * (winding * handedness);
    return frame;
}

#if (ANY_BIPLANAR == 1) || (F_WORLD_OVERLAY > 0)
    // Axis permutations for the biggest and smallest components of the normal, plus the
    // remaining one. Shared by the biplanar layer projection and the world overlay.
    struct PlanarAxes_t
    {
        ivec3 Major;
        ivec3 Median;
        vec3 AbsNormal;
    };

    PlanarAxes_t GetPlanarAxes(vec3 normal)
    {
        vec3 absNormal = abs(normal);

        ivec3 major = (absNormal.x > absNormal.y && absNormal.x > absNormal.z)
            ? ivec3(0, 1, 2)
            : ((absNormal.y > absNormal.z) ? ivec3(1, 2, 0) : ivec3(2, 0, 1));

        ivec3 minor = (absNormal.x < absNormal.y && absNormal.x < absNormal.z)
            ? ivec3(0, 1, 2)
            : ((absNormal.y < absNormal.z) ? ivec3(1, 2, 0) : ivec3(2, 0, 1));

        PlanarAxes_t axes;
        axes.Major = major;
        axes.Median = ivec3(3) - minor - major;
        axes.AbsNormal = absNormal;
        return axes;
    }

    vec2 GetPlanarTexCoord(vec3 position, ivec3 axis)
    {
        vec2 planar = vec2(position[axis.y], position[axis.z]);

        if (axis.x == 1)
        {
            planar = planar.yx;
        }

        return vec2(planar.x, -planar.y);
    }

    // Blend between the two planes once either is more than ~35 degrees off its axis.
    vec2 GetPlanarWeights(PlanarAxes_t axes)
    {
        vec2 weights = saturate((vec2(axes.AbsNormal[axes.Major.x], axes.AbsNormal[axes.Median.x]) - 0.5773) * 2.3657441);
        return weights / (weights.x + weights.y);
    }
#endif

#if (ANY_BIPLANAR == 1)
    // Layers only ever sample the dominant plane. Valve swaps in the median plane's
    // derivatives when that plane wins outright but keeps the dominant plane's coordinate,
    // so there is only ever one set of texels to fetch.
    LayerFrame_t GetBiPlanarFrame(vec3 normal, float tiling)
    {
        PlanarAxes_t axes = GetPlanarAxes(normal);
        vec2 texCoord = GetPlanarTexCoord(vFragPosition, axes.Major) / ((tiling == 0.0) ? 1.0 : tiling);
        return GetDerivedFrame(texCoord, normal);
    }
#endif

#if (F_WORLD_OVERLAY > 0)
    #if (WORLD_OVERLAY_OBJECT_SPACE == 1)
        #define WorldOverlayPosition vPositionOs
    #else
        #define WorldOverlayPosition vFragPosition
    #endif

    vec4 SampleBiPlanar(sampler2D overlayTexture, vec3 normal, float tiling)
    {
        PlanarAxes_t axes = GetPlanarAxes(normal);
        float invTiling = 1.0 / ((tiling == 0.0) ? 1.0 : tiling);

        vec2 majorUv = GetPlanarTexCoord(WorldOverlayPosition, axes.Major) * invTiling;
        vec2 medianUv = GetPlanarTexCoord(WorldOverlayPosition, axes.Median) * invTiling;

        vec3 positionDdx = dFdxFine(WorldOverlayPosition) * invTiling;
        vec3 positionDdy = dFdyFine(WorldOverlayPosition) * invTiling;

        vec4 major = textureGrad(overlayTexture, majorUv,
            vec2(positionDdx[axes.Major.y], positionDdx[axes.Major.z]),
            vec2(positionDdy[axes.Major.y], positionDdy[axes.Major.z]));
        vec4 median = textureGrad(overlayTexture, medianUv,
            vec2(positionDdx[axes.Median.y], positionDdx[axes.Median.z]),
            vec2(positionDdy[axes.Median.y], positionDdy[axes.Median.z]));

        return mix(major, median, GetPlanarWeights(axes).y);
    }
#endif

#if (F_UV_OVERLAY == 1) || (F_WORLD_OVERLAY > 0)
    // An overlay texel is a signed multiplier around 0.5. Values above brighten and values
    // below darken, each side shaped by its own contrast exponent.
    vec3 GetOverlayFactor(vec3 overlay, float darknessContrast, float brightnessContrast)
    {
        vec3 signedOverlay = overlay * 2.0 - 1.0;

        vec3 brighten = (vec3(1.0) - SafePow(vec3(1.0) - max(vec3(0.0), signedOverlay), vec3(brightnessContrast))) * brightnessContrast;
        vec3 darken = (SafePow(vec3(1.0) + min(vec3(0.0), signedOverlay), vec3(darknessContrast)) - vec3(1.0)) * darknessContrast;

        return max(vec3(0.0), brighten + darken + vec3(1.0));
    }

    // "0=Unmasked, 1=Masked, 2=Inverted"
    float GetOverlayTintMask(int mode, float tintMask)
    {
        return (mode == 0) ? 1.0 : ((mode == 2) ? (1.0 - tintMask) : tintMask);
    }
#endif

struct Layer_t
{
    vec3 Albedo;
    vec3 NormalOffset; // world space surface gradient, subtracted from the geometric normal
    float Roughness;
    float Metalness;
    float TintMask;
    float SelfIllum;
};

// The hemi-octahedron normal expressed as a surface gradient, so that layers and overlays
// can be summed before a single normalize at the end.
vec2 GetSurfaceGradient(vec2 hemiOct)
{
    vec3 normalTs = DecodeHemiOctahedronNormal(hemiOct);
    vec3 absNormal = abs(normalTs);
    return -normalTs.xy / max(absNormal.z, 0.0078125 * max(absNormal.x, absNormal.y));
}

float ApplyContrastBrightness(float value, vec2 contrastBrightness)
{
    return saturate(((value - 0.5) + contrastBrightness.y) * contrastBrightness.x + 0.5);
}

Layer_t SampleLayer(
    sampler2D colorTexture, sampler2D normalRoughnessTexture, sampler2D packedTexture,
    LayerFrame_t frame,
    mat4 albedoColorCorrect, mat4 colorTint,
    vec2 roughnessContrastBrightness, vec2 tintMaskContrastBrightness, vec2 metalnessContrastBrightness,
    float normalStrength, float selfIllumScale,
    bool maskColorTint, bool maskVertexColorTint, bool modelTint, float vertexColorStrength)
{
    vec4 color = texture(colorTexture, frame.TexCoord);
    vec4 normalRoughness = texture(normalRoughnessTexture, frame.TexCoord);
    vec4 packedTexel = texture(packedTexture, frame.TexCoord);

    Layer_t layer;
    layer.TintMask = ApplyContrastBrightness(color.a, tintMaskContrastBrightness);
    layer.Roughness = ApplyContrastBrightness(normalRoughness.b, roughnessContrastBrightness);
    layer.Metalness = ApplyContrastBrightness(packedTexel.g, metalnessContrastBrightness);
    layer.SelfIllum = packedTexel.r * selfIllumScale;

    vec2 gradient = GetSurfaceGradient(normalRoughness.rg) * normalStrength;
    layer.NormalOffset = frame.Tangent * gradient.x + frame.Bitangent * gradient.y;

    vec3 corrected = (vec4(color.rgb, 1.0) * albedoColorCorrect).rgb;
    vec3 tinted = (vec4(corrected, 1.0) * colorTint).rgb;
    vec3 albedo = mix(corrected, tinted, maskColorTint ? layer.TintMask : 1.0);

    float vertexColorAmount = modelTint
        ? ((maskVertexColorTint ? layer.TintMask : 1.0) * vertexColorStrength)
        : 0.0;
    layer.Albedo = mix(albedo, albedo * vVertexColorOut.rgb, vertexColorAmount);

    return layer;
}

// Where along the vertex paint gradient this layer starts showing through. The reveal mask
// is first stretched around its midpoint by the offset, then squeezed into the band the
// softness leaves free at either end.
float GetRevealThreshold(float revealMask, bool invert, float offset, float softness)
{
    float reveal = invert ? (1.0 - revealMask) : revealMask;

    float halfOffset = offset * 0.5;
    float mid = 0.5 + halfOffset;
    float remapped = (reveal < 0.5)
        ? saturate(reveal * 2.0) * mid
        : saturate((reveal - 0.5) * 2.0) * (0.5 - halfOffset) + mid;

    return saturate(clamp(remapped, 1e-4, 0.9999)) * (1.0 - 2.0 * softness) + softness;
}

// Per-layer strength of the seam decoration around a reveal edge. x decorates layer 1,
// y this layer, z layer 3.
vec3 GetBorderAmount(float blend, float threshold, vec2 width, vec2 softness, vec3 layerAmount)
{
    bool below = blend < threshold;

    float edgeSoftness = below ? softness.x : softness.y;
    float outer = below ? max(1e-4, threshold - width.x) : min(0.9999, threshold + width.y);
    float inner = below ? max(1e-4, threshold - 0.5 * width.x) : min(0.9999, threshold + 0.5 * width.y);

    return layerAmount * smoothstep(mix(inner, outer, edgeSoftness), mix(inner, threshold, edgeSoftness), blend);
}

vec3 ApplyBorderTint(vec3 albedo, vec4 tint, float amount, int blendMode)
{
    float weight = amount * tint.a;

    if (blendMode == 1) // Mod2x
    {
        return albedo * mix(vec3(1.0), tint.rgb * 1.9921569, weight);
    }
    else if (blendMode == 2) // Blend
    {
        return mix(albedo, tint.rgb, weight);
    }

    return albedo * mix(vec3(1.0), tint.rgb, weight); // Multiply
}

MaterialProperties_t GetMaterial(vec3 vertexNormal)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormal, vFragPosition, vTangentOut, vBitangentOut);

    // Every layer above the first is revealed by its own mask weighed against a channel of
    // the vertex paint.
    float blend2 = vColorBlendValues.x;
    #if (F_LAYER_COUNT == 1)
        float blend3 = vColorBlendValues.y;
        blend2 = (blend3 < 1.0) ? blend2 : 0.0;
        blend3 = (blend3 > 1e-5) ? blend3 : 0.0;
    #endif
    blend2 = (blend2 > 1e-5) ? blend2 : 0.0;

    #if (F_UV_OVERLAY == 1)
        vec2 overlayTexCoord = ApplyUvTransform(
            g_bUseSecondaryUvForOverlay ? vTexCoord.zw : vTexCoord.xy,
            g_vOverlayScaleRotationTranslation);

        float revealOverlay = texture(g_tRevealOverlay, overlayTexCoord).r;
        revealOverlay = g_bRevealOverlayInvert2 ? (1.0 - revealOverlay) : revealOverlay;
        blend2 = saturate(blend2 + revealOverlay * g_flRevealOverlayStrength2);
    #endif

    #if (F_LAYER1_BIPLANAR == 1)
        LayerFrame_t frame1 = GetBiPlanarFrame(mat.GeometricNormal, g_flBiPlanarTiling1);
    #else
        LayerFrame_t frame1 = GetMeshFrame(ApplyUvTransform(vTexCoord.xy, g_vScaleRotationTranslation1));
    #endif

    Layer_t layer1 = SampleLayer(g_tColor1, g_tNormalRoughness1, g_tPacked1, frame1,
        g_mAlbedoColorCorrect1, g_mTextureColorTint1,
        g_vLayerRoughnessContrastBrightness1, g_vLayerTintMaskContrastBrightness1, g_vLayerMetalnessContrastBrightness1,
        g_fLayerNormalStrength1, g_fLayerSelfIllumScale1,
        g_bMaskColorTint1, g_bMaskVertexColorTint1, g_bModelTint1, g_fVertexColorStrength1);

    #if (F_LAYER2_BIPLANAR == 1)
        LayerFrame_t frame2 = GetBiPlanarFrame(mat.GeometricNormal, g_flBiPlanarTiling2);
    #else
        LayerFrame_t frame2 = GetMeshFrame(ApplyUvTransform(vTexCoord.xy, g_vScaleRotationTranslation2));
    #endif

    Layer_t layer2 = SampleLayer(g_tColor2, g_tNormalRoughness2, g_tPacked2, frame2,
        g_mAlbedoColorCorrect2, g_mTextureColorTint2,
        g_vLayerRoughnessContrastBrightness2, g_vLayerTintMaskContrastBrightness2, g_vLayerMetalnessContrastBrightness2,
        g_fLayerNormalStrength2, g_fLayerSelfIllumScale2,
        g_bMaskColorTint2, g_bMaskVertexColorTint2, g_bModelTint2, g_fVertexColorStrength2);

    #if (F_LAYER_COUNT == 1)
        #if (LAYER3_BIPLANAR == 1)
            LayerFrame_t frame3 = GetBiPlanarFrame(mat.GeometricNormal, g_flBiPlanarTiling3);
        #else
            LayerFrame_t frame3 = GetMeshFrame(ApplyUvTransform(vTexCoord.xy, g_vScaleRotationTranslation3));
        #endif

        Layer_t layer3 = SampleLayer(g_tColor3, g_tNormalRoughness3, g_tPacked3, frame3,
            g_mAlbedoColorCorrect3, g_mTextureColorTint3,
            g_vLayerRoughnessContrastBrightness3, g_vLayerTintMaskContrastBrightness3, g_vLayerMetalnessContrastBrightness3,
            g_fLayerNormalStrength3, g_fLayerSelfIllumScale3,
            g_bMaskColorTint3, g_bMaskVertexColorTint3, g_bModelTint3, g_fVertexColorStrength3);
    #endif

    // Self illumination is taken before any seam or overlay decoration touches the albedo.
    vec3 illumColor = layer1.Albedo * layer1.SelfIllum * g_vLayerSelfIllumTint1;
    vec3 illumColor2 = layer2.Albedo * layer2.SelfIllum * g_vLayerSelfIllumTint2;
    #if (F_LAYER_COUNT == 1)
        vec3 illumColor3 = layer3.Albedo * layer3.SelfIllum * g_vLayerSelfIllumTint3;
    #endif

    float revealMask2 = texture(g_tRevealMask2, ApplyUvTransform(
        g_bRevealUseSecondaryUv2 ? vTexCoord.zw : vTexCoord.xy,
        g_vRevealScaleRotationTranslation2)).r;
    float threshold2 = GetRevealThreshold(revealMask2, g_bRevealInvert2, g_flRevealOffset2, g_flRevealSoftness2);

    #if (F_LAYER_COUNT == 1)
        float revealMask3 = texture(g_tRevealMask3, ApplyUvTransform(
            g_bRevealUseSecondaryUv3 ? vTexCoord.zw : vTexCoord.xy,
            g_vRevealScaleRotationTranslation3)).r;
        float threshold3 = GetRevealThreshold(revealMask3, g_bRevealInvert3, g_flRevealOffset3, g_flRevealSoftness3);
    #endif

    // Border effects
    if (g_bBorderEffects2 && blend2 > 0.0)
    {
        vec3 amount = GetBorderAmount(blend2, threshold2, g_vBorderWidth2, g_vBorderSoftness2, vec3(g_vBorderLayerAmount2, 0.0));

        layer1.Albedo = ApplyBorderTint(layer1.Albedo, g_vBorderTint2, amount.x, g_nBorderBlendMode2);
        layer2.Albedo = ApplyBorderTint(layer2.Albedo, g_vBorderTint2, amount.y, g_nBorderBlendMode2);
        layer1.Roughness = mix(layer1.Roughness, g_flBorderRoughness2, g_flBorderRoughnessStrength2 * amount.x);
        layer2.Roughness = mix(layer2.Roughness, g_flBorderRoughness2, g_flBorderRoughnessStrength2 * amount.y);
    }

    #if (F_LAYER_COUNT == 1)
        if (g_bBorderEffects3 && blend3 > 0.0)
        {
            vec3 amount = GetBorderAmount(blend3, threshold3, g_vBorderWidth3, g_vBorderSoftness3, g_vBorderLayerAmount3);

            layer1.Albedo = ApplyBorderTint(layer1.Albedo, g_vBorderTint3, amount.x, g_nBorderBlendMode3);
            layer2.Albedo = ApplyBorderTint(layer2.Albedo, g_vBorderTint3, amount.y, g_nBorderBlendMode3);
            layer3.Albedo = ApplyBorderTint(layer3.Albedo, g_vBorderTint3, amount.z, g_nBorderBlendMode3);
            layer1.Roughness = mix(layer1.Roughness, g_flBorderRoughness3, g_flBorderRoughnessStrength3 * amount.x);
            layer2.Roughness = mix(layer2.Roughness, g_flBorderRoughness3, g_flBorderRoughnessStrength3 * amount.y);
            layer3.Roughness = mix(layer3.Roughness, g_flBorderRoughness3, g_flBorderRoughnessStrength3 * amount.z);
        }
    #endif

    #if (F_WORLD_OVERLAY > 0)
        for (int i = 0; i < F_WORLD_OVERLAY; i++)
        {
            #if (F_WORLD_OVERLAY == 2)
                vec4 overlay = (i == 1)
                    ? SampleBiPlanar(g_tColorWorldOverlay2, mat.GeometricNormal, g_vWorldOverlayTiling[i])
                    : SampleBiPlanar(g_tColorWorldOverlay, mat.GeometricNormal, g_vWorldOverlayTiling[i]);
                vec3 factor = GetOverlayFactor(overlay.rgb, g_vWorldOverlayDarknessContrast[i], g_vWorldOverlayBrightnessContrast[i]);
                float strength1 = g_vWorldOverlayStrengths1[i];
                float strength2 = g_vWorldOverlayStrengths2[i];
            #else
                vec4 overlay = SampleBiPlanar(g_tColorWorldOverlay, mat.GeometricNormal, g_flWorldOverlayTiling);
                vec3 factor = GetOverlayFactor(overlay.rgb, g_flWorldOverlayDarknessContrast, g_flWorldOverlayBrightnessContrast);
                float strength1 = g_flWorldOverlayStrength1;
                float strength2 = g_flWorldOverlayStrength2;
            #endif

            layer1.Albedo = mix(layer1.Albedo, layer1.Albedo * factor, GetOverlayTintMask(g_nWorldOverlayTintMask1, layer1.TintMask) * strength1);
            layer2.Albedo = mix(layer2.Albedo, layer2.Albedo * factor, GetOverlayTintMask(g_nWorldOverlayTintMask2, layer2.TintMask) * strength2);

            #if (F_LAYER_COUNT == 1)
                #if (F_WORLD_OVERLAY == 2)
                    float strength3 = g_vWorldOverlayStrengths3[i];
                #else
                    float strength3 = g_flWorldOverlayStrength3;
                #endif
                layer3.Albedo = mix(layer3.Albedo, layer3.Albedo * factor, GetOverlayTintMask(g_nWorldOverlayTintMask3, layer3.TintMask) * strength3);
            #endif
        }
    #endif

    #if (F_UV_OVERLAY == 1)
    {
        // The overlay carries its own tangent frame unless it rides on the untransformed
        // primary UVs, in which case the mesh frame is what the layers already use.
        bool derived = g_bUseSecondaryUvForOverlay || any(notEqual(g_vOverlayScaleRotationTranslation, vec4(1.0, 0.0, 0.0, 0.0)));
        LayerFrame_t overlayFrame = GetDerivedFrame(overlayTexCoord, mat.GeometricNormal);
        vec3 overlayTangent = derived ? overlayFrame.Tangent : vTangentOut;
        vec3 overlayBitangent = derived ? overlayFrame.Bitangent : vBitangentOut;

        vec4 overlayColor = texture(g_tColorOverlay, overlayTexCoord);
        vec4 overlayNormalRoughness = texture(g_tNormalRoughnessOverlay, overlayTexCoord);

        vec2 overlayGradient = GetSurfaceGradient(overlayNormalRoughness.rg);
        vec3 overlayNormalOffset = overlayTangent * overlayGradient.x + overlayBitangent * overlayGradient.y;
        float overlayRoughness = overlayNormalRoughness.b * 2.0 - 1.0;

        vec3 factor = GetOverlayFactor(overlayColor.rgb, g_flOverlayDarknessContrast, g_flOverlayBrightnessContrast);

        float mask1 = GetOverlayTintMask(g_nOverlayTintMask1, layer1.TintMask);
        layer1.Albedo = mix(layer1.Albedo, layer1.Albedo * factor, mask1 * g_flOverlayStrength1);
        layer1.NormalOffset += overlayNormalOffset * (g_flOverlayNormalStrength1 * mask1);
        layer1.Roughness = saturate(layer1.Roughness + overlayRoughness * g_flOverlayRoughnessStrength1 * mask1);

        float mask2 = GetOverlayTintMask(g_nOverlayTintMask2, layer2.TintMask);
        layer2.Albedo = mix(layer2.Albedo, layer2.Albedo * factor, mask2 * g_flOverlayStrength2);
        layer2.NormalOffset += overlayNormalOffset * (g_flOverlayNormalStrength2 * mask2);
        layer2.Roughness = saturate(layer2.Roughness + overlayRoughness * g_flOverlayRoughnessStrength2 * mask2);

        #if (F_LAYER_COUNT == 1)
            float mask3 = GetOverlayTintMask(g_nOverlayTintMask3, layer3.TintMask);
            layer3.Albedo = mix(layer3.Albedo, layer3.Albedo * factor, mask3 * g_flOverlayStrength3);
            layer3.NormalOffset += overlayNormalOffset * (g_flOverlayNormalStrength3 * mask3);
            layer3.Roughness = saturate(layer3.Roughness + overlayRoughness * g_flOverlayRoughnessStrength3 * mask3);
        #endif
    }
    #endif

    // Composite
    float weight2 = smoothstep(-g_flRevealSoftness2, g_flRevealSoftness2, blend2 - threshold2);

    Layer_t result = layer1;
    result.Albedo = mix(layer1.Albedo, layer2.Albedo, weight2);
    result.NormalOffset = mix(layer1.NormalOffset, layer2.NormalOffset, weight2);
    result.Roughness = mix(layer1.Roughness, layer2.Roughness, weight2);
    result.Metalness = mix(layer1.Metalness, layer2.Metalness, weight2);
    result.SelfIllum = mix(layer1.SelfIllum, layer2.SelfIllum, weight2);
    illumColor = mix(illumColor, illumColor2, weight2);

    #if (F_LAYER_COUNT == 1)
        float weight3 = smoothstep(-g_flRevealSoftness3, g_flRevealSoftness3, blend3 - threshold3);

        result.Albedo = mix(result.Albedo, layer3.Albedo, weight3);
        result.NormalOffset = mix(result.NormalOffset, layer3.NormalOffset, weight3);
        result.Roughness = mix(result.Roughness, layer3.Roughness, weight3);
        result.Metalness = mix(result.Metalness, layer3.Metalness, weight3);
        result.SelfIllum = mix(result.SelfIllum, layer3.SelfIllum, weight3);
        illumColor = mix(illumColor, illumColor3, weight3);
    #endif

    mat.Albedo = result.Albedo;
    mat.IllumColor = illumColor;
    mat.Metalness = result.Metalness;
    mat.RoughnessTex = vec2(result.Roughness);

    // The layers accumulate world space surface gradients, so the tangent space normal is
    // recovered from the total rather than blended per layer.
    vec2 gradient = vec2(dot(result.NormalOffset, vTangentOut), dot(result.NormalOffset, vBitangentOut));
    mat.NormalMap = normalize(vec3(-gradient, 1.0));
    mat.Normal = normalize(mat.GeometricNormal - result.NormalOffset);

    AdjustRoughnessByGeometricNormal(mat);

    mat.AmbientNormal = mat.Normal;
    mat.AmbientGeometricNormal = mat.GeometricNormal;

    mat.DiffuseColor = mat.Albedo - mat.Albedo * mat.Metalness;
    mat.SpecularColor = GetSpecularColorCitadel(mat.Albedo, mat.Metalness);

    mat.AmbientOcclusion = 1.0;
    mat.DiffuseAO = vec3(1.0);
    mat.SpecularAO = 1.0;

    return mat;
}

void main()
{
    vec3 vertexNormal = SwitchCentroidNormal(vNormalOut, vCentroidNormalOut);

    MaterialProperties_t mat = GetMaterial(vertexNormal);

    LightingTerms_t lighting = CalculateLighting(mat);
    ApplyAmbientOcclusion(lighting, mat);

    vec3 diffuseLighting = lighting.DiffuseDirect + lighting.DiffuseIndirect;
    vec3 specularLighting = lighting.SpecularDirect + lighting.SpecularIndirect;

    vec3 combinedLighting = mat.DiffuseColor * diffuseLighting + specularLighting + mat.IllumColor;

    ApplyFog(combinedLighting, mat.PositionWS);

    outputColor = vec4(combinedLighting, 1.0);

    if (HandleMaterialRenderModes(outputColor, mat)
    || HandleLightingRenderModes(outputColor, mat, lighting)
    || HandleUVRenderModes(outputColor, mat, g_tColor1, vTexCoord.xy))
    {
        //
    }
    else if (g_iRenderMode == renderMode_Cubemaps)
    {
        // No bumpmaps, full reflectivity
        vec3 viewmodeEnvMap = GetEnvironment(mat).rgb;
        outputColor.rgb = SrgbLinearToGamma(viewmodeEnvMap);
    }
    else if (g_iRenderMode == renderMode_Tint)
    {
        outputColor = vec4(vVertexColorOut.rgb, vVertexColorOut.a);
    }
    else if (g_iRenderMode == renderMode_VertexColor)
    {
        outputColor.rgb = vVertexColorOut.rgb;
    }
    else if (g_iRenderMode == renderMode_TerrainBlend)
    {
        outputColor.rgb = vec3(vColorBlendValues, 0.0);
    }
}
