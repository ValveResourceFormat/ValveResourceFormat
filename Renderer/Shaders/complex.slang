#version 460

import common.utils;

#include "complex.vert.slang"

import common.features;
import common.ViewConstants;
import common.LightingConstants;

import common.lighting_common;
import common.fullbright;
import common.texturing;
import common.fog;
import common.lighting;
import common.environment;
import common.pbr;

import features.csgo_character_eyes_ps;

//Parameter defines - These are default values and can be overwritten based on material/model parameters
// BLENDING
extern static const int F_FULLBRIGHT = 0;
extern static const int F_LIT = 0;
extern static const int F_UNLIT = 0;
extern static const int F_ALPHA_TEST = 0;
extern static const int F_TRANSLUCENT = 0;
extern static const int F_BLEND_MODE = 0;
extern static const int F_GLASS = 0;
uniform int F_DISABLE_TONE_MAPPING;
extern static const int F_SCALE_NORMAL_MAP = 0;
// TEXTURING
extern static const int F_TINT_MASK = 0;
extern static const int F_FANCY_BLENDING = 0;
extern static const int F_METALNESS_TEXTURE = 0;
extern static const int F_AMBIENT_OCCLUSION_TEXTURE = 0;
extern static const int F_SELF_ILLUM = 0;
extern static const int F_ENABLE_AMBIENT_OCCLUSION = 0;
extern static const int F_ENABLE_TINT_MASKS = 0;
extern static const int F_DECAL_TEXTURE = 0;
uniform int F_DECAL_BLEND_MODE;
// SHADING
extern static const int F_SPECULAR = 0;
extern static const int F_SPECULAR_INDIRECT = 0;
extern static const int F_ANISOTROPIC_GLOSS = 0;
extern static const int F_USE_BENT_NORMALS = 0;
extern static const int F_DIFFUSE_WRAP = 0;
extern static const int F_TRANSMISSIVE_BACKFACE_NDOTL = 0;
uniform int F_NO_SPECULAR_AT_FULL_ROUGHNESS;

// vr_standard
extern static const int F_HIGH_QUALITY_GLOSS = 0;
extern static const int F_BLEND_NORMALS = 0;

static const int ANISO_ROUGHNESS = ((F_ANISOTROPIC_GLOSS == 1) || (vr_standard_vfx != 0 && F_HIGH_QUALITY_GLOSS == 1)) ? 1 : 0;

// Derived material property booleans
static const bool _uniformMetalness = (simple_vfx_common || complex_vfx_common) && (F_METALNESS_TEXTURE == 0);
static const bool _colorAlphaMetalness = ((simple_vfx_common || complex_vfx_common) && (F_METALNESS_TEXTURE == 1)) || (pbr_vfx != 0);
static const bool _colorAlphaAO = ((vr_simple_vfx != 0) && (F_AMBIENT_OCCLUSION_TEXTURE == 1) && (F_METALNESS_TEXTURE == 0)) || (simple_blend_common && (F_ENABLE_AMBIENT_OCCLUSION == 1));
static const bool _metalnessTexture = (complex_vfx_common && (F_METALNESS_TEXTURE == 1) && ((F_ALPHA_TEST == 1) || (F_TRANSLUCENT == 1))) || (csgo_weapon_vfx != 0) || (csgo_character_vfx != 0) || (csgo_vertexlitgeneric_vfx != 0);
static const bool _ambientOcclusionTexture = ((vr_simple_vfx != 0) && (F_AMBIENT_OCCLUSION_TEXTURE == 1) && (F_METALNESS_TEXTURE == 1)) || complex_vfx_common || (csgo_foliage_vfx != 0) || (csgo_weapon_vfx != 0) || (csgo_character_vfx != 0) || csgo_generic_vfx_common || (pbr_vfx != 0);

static const bool isUnlit = (vr_unlit_vfx != 0) || (unlit_vfx != 0) || (csgo_unlitgeneric_vfx != 0) || (F_FULLBRIGHT == 1) || (F_UNLIT == 1) || (static_overlay_vfx_common && F_LIT == 0) || (csgo_decalmodulate_vfx != 0);
static const bool isAlphaTest = (F_ALPHA_TEST == 1) || (((csgo_unlitgeneric_vfx != 0) || static_overlay_vfx_common) && (F_BLEND_MODE == 2)) || (csgo_decalmodulate_vfx != 0);
static const bool isTranslucent = (F_TRANSLUCENT == 1) || (F_GLASS == 1) || (F_BLEND_MODE > 0 && F_BLEND_MODE != 2) || glass_vfx_common || (csgo_decalmodulate_vfx != 0) || (((csgo_unlitgeneric_vfx != 0) || static_overlay_vfx_common) && (F_BLEND_MODE == 1));
static const bool isSelfIllum = ((F_SELF_ILLUM == 1 && ((generic_vfx != 0) || complex_vfx_common || (csgo_vertexlitgeneric_vfx != 0) || (vr_skin_vfx != 0))) || (csgo_unlitgeneric_vfx != 0));
static const bool isBlendMod2x = (F_BLEND_MODE == 3) || (csgo_decalmodulate_vfx != 0);
static const bool isBlendModThenAdd = (F_BLEND_MODE == 6);

static const bool csgo_generic_blend = csgo_generic_vfx_common && (F_LAYERS > 0);
static const bool terrain_blend_common = simple_blend_common || csgo_generic_blend || vr_standard_vfx_blend || (environment_blend_vfx != 0);

// IBL
static const int IBL = (vr_complex_vfx != 0) ? F_SPECULAR
                      : csgo_generic_vfx_common ? F_SPECULAR_INDIRECT
                      : (generic_vfx != 0) ? 0
                      : 1;

// Textures
Sampler2D g_tBlueNoise;
[SrgbRead] Sampler2D g_tColor;
Sampler2D g_tNormal;
Sampler2D g_tTintMask;

// Foliage AO
[Default(1.0)] uniform float g_flVertexAmbientOcclusionPower;
uniform float g_flVertexAmbientOcclusionAmount;

// Transmissive backface
uniform int F_DISABLE_TRANSMISSIVE_SHADOWS;
uniform int F_USE_ALBEDO_FOR_TRANSMISSIVE;
[SrgbRead] Sampler2D g_tTransmissiveColor;

// Terrain/blend textures
[SrgbRead] Sampler2D g_tLayer2Color;
Sampler2D g_tLayer2NormalRoughness;
[Default2(1.0, 1.0)] uniform vec2 g_vTexCoordScale2;

// Skin textures
Sampler2D g_tCombinedMasks;
[Default3(0.74902, 0.231373, 0.011765)] [SrgbRead] uniform vec3 g_vTransmissionColor;
[Default(1.0)] uniform float g_flMouthInteriorBrightnessScale;

// Opacity
[Default(1.0)] uniform float g_flOpacityScale;

// Glass remap
[Default2(0.0, 1.0)] uniform vec2 g_flTranslucencyRemap;

// Metalness
uniform float g_flMetalness;
Sampler2D g_tMetalness;
Sampler2D g_tMetalnessReflectance;

// Simple blend
Sampler2D g_tMask;
uniform float g_flMetalnessA;
uniform float g_flMetalnessB;
uniform float g_BlendFalloff;
uniform float g_BlendHeight;

// Fancy blending
Sampler2D g_tBlendModulation;
uniform float g_flBlendSoftness;

// Environment blend
Sampler2D g_tPacked1;
Sampler2D g_tPacked2;
Sampler2D g_tRevealMask2;
uniform float g_flRevealSoftness2;

// vr_standard
Sampler2D g_tNormal2;
Sampler2D g_tGloss;
[SrgbRead] Sampler2D g_tLayer1Color;
[SrgbRead] Sampler2D g_tLayer1Color2;
[SrgbRead] Sampler2D g_tLayer2Color2;
Sampler2D g_tLayer1RevealMask;
[Default(0.5)] uniform float g_flLayer1BlendSoftness;
Sampler2D g_tLayer1Normal;

// Retro reflectivity
[Default(1.0)] uniform float g_flRetroReflectivity;

// Normal map scale
[Default(1.0)] uniform float g_flNormalMapScaleFactor;
[Default(1.0)] uniform float g_flBumpStrength;

// AO texture
Sampler2D g_tAmbientOcclusion;

// Anisotropic gloss
Sampler2D g_tAnisoGloss;

// Secondary UV controls
uniform bool g_bUseSecondaryUvForAmbientOcclusion;
uniform bool g_bUseSecondaryUvForTintMask;
uniform bool g_bUseSecondaryUvForDetailMask;
uniform bool g_bUseSecondaryUvForSelfIllum;

// Self-illum
Sampler2D g_tSelfIllumMask;
uniform float g_flSelfIllumAlbedoFactor;
uniform float g_flSelfIllumBrightness;
[Default(1.0)] uniform float g_flSelfIllumScale;
uniform vec2 g_vSelfIllumScrollSpeed;
[Default3(1.0, 1.0, 1.0)] [SrgbRead] uniform vec3 g_vSelfIllumTint;

vec3 GetStandardSelfIllumination(float flSelfIllumMask, vec3 vAlbedo)
{
    vec3 selfIllumScale = (exp2(g_flSelfIllumBrightness) * g_flSelfIllumScale) * g_vSelfIllumTint.rgb;
    return selfIllumScale * flSelfIllumMask * mix(vec3(1.0), vAlbedo, g_flSelfIllumAlbedoFactor);
}

// Alpha test
[Default(0.5)] uniform float g_flAlphaTestReference;
[Default(1.0)] uniform float g_flAntiAliasedEdgeStrength;

float AlphaTestAntiAliasing(float flOpacity, vec2 UVs)
{
    float flAlphaTestAA = saturate( (flOpacity - g_flAlphaTestReference) / ClampToPositive( fwidth(flOpacity) ) + 0.5 );
    float flAlphaTestAA_Amount = min(1.0, length( fwidth(UVs) ) * 4.0);
    float flAntiAliasAlphaBlend = mix(1.0, flAlphaTestAA_Amount, g_flAntiAliasedEdgeStrength);
    return mix( flAlphaTestAA, flOpacity, flAntiAliasAlphaBlend );
}

// Detail texture
[Default(1.0)] uniform float g_flDetailBlendFactor;
uniform float g_flDetailBlendToFull;
[Default(1.0)] uniform float g_flDetailNormalStrength;
Sampler2D g_tDetailMask;
[SrgbRead] Sampler2D g_tDetail;
Sampler2D g_tNormalDetail;

#define MOD2X_MUL 1.9922
#define DETAIL_CONST 0.9961

void applyDetailTexture(inout vec3 Albedo, inout vec3 NormalMap, vec2 detailMaskCoords, vec2 detailTexCoords)
{
    float detailMask = texture(g_tDetailMask, detailMaskCoords).x;
    detailMask = g_flDetailBlendFactor * max(detailMask, g_flDetailBlendToFull);

    if (F_DETAIL_TEXTURE == 1) // MOD2X
    {
        vec3 DetailTexture = texture(g_tDetail, detailTexCoords).rgb * MOD2X_MUL;
        Albedo *= mix(vec3(1.0), DetailTexture, detailMask);
    }
    else if (F_DETAIL_TEXTURE == 2 || F_DETAIL_TEXTURE == 4) // OVERLAY
    {
        vec3 DetailTexture = DETAIL_CONST * texture(g_tDetail, detailTexCoords).rgb;
        vec3 linearAlbedo = SrgbLinearToGamma(Albedo);
        vec3 overlayScreen = 1.0 - (1.0 - DetailTexture) * (1.0 - linearAlbedo) * 2.0;
        vec3 overlayMul = DetailTexture * linearAlbedo * 2.0;
        vec3 linearBlendedOverlay = mix(overlayMul, overlayScreen, greaterThanEqual(linearAlbedo, vec3(0.5)));
        vec3 gammaBlendedOverlay = SrgbGammaToLinear(linearBlendedOverlay);
        Albedo = mix(Albedo, gammaBlendedOverlay, detailMask);
    }

    if (F_DETAIL_TEXTURE == 3 || F_DETAIL_TEXTURE == 4) // NORMALS
    {
        vec3 DetailNormal = DecodeHemiOctahedronNormal(texture(g_tNormalDetail, detailTexCoords).xy);
        DetailNormal = mix(vec3(0, 0, 1), DetailNormal, detailMask * g_flDetailNormalStrength);
        NormalMap = NormalMap * DetailNormal.z + vec3(NormalMap.z * DetailNormal.z * DetailNormal.xy, 0.0);
    }
}

// Glass
uniform bool g_bFresnel;
[Default(3.0)] uniform float g_flEdgeColorFalloff;
[Default(0.5)] uniform float g_flEdgeColorMaxOpacity;
[Default(0.1)] uniform float g_flEdgeColorThickness;
[Default3(0.5, 0.8, 0.5)] [SrgbRead] uniform vec3 g_vEdgeColor;
[Default(0.1)] uniform float g_flRefractScale;

vec4 GetGlassMaterial(MaterialProperties_t mat)
{
    float viewDotNormalInv = saturate(1.0 - (dot(mat.ViewDir, mat.Normal) - g_flEdgeColorThickness));
    float fresnel = saturate(pow(viewDotNormalInv, g_flEdgeColorFalloff)) * g_flEdgeColorMaxOpacity;
    vec4 fresnelColor = vec4(g_vEdgeColor.xyz, g_bFresnel ? fresnel : 0.0);
    return mix(vec4(mat.Albedo, mat.Opacity), fresnelColor, g_flOpacityScale);
}

// Cloth Sheen
[Default(0.667)] uniform float g_flSheenScale;
[Default3(1.0, 1.0, 1.0)] [SrgbRead] uniform vec3 g_flSheenTintColor;

vec3 ApplySheen(float reflectance, vec3 albedo, float clothMask)
{
    return mix(vec3(reflectance), saturate((g_flSheenTintColor.rgb * sqrt(albedo)) * g_flSheenScale), clothMask);
}

// Decal
Sampler2D g_tDecal;
uniform bool g_bUseSecondaryUvForDecal;

vec3 ApplyDecalTexture(vec3 albedo, vec2 texCoord, vec2 texCoord2)
{
    vec2 coords;
    if ((F_SECONDARY_UV == 1) || (F_FORCE_UV2 == 1))
        coords = (g_bUseSecondaryUvForDecal || (F_FORCE_UV2 == 1)) ? texCoord2 : texCoord;
    else
        coords = texCoord;
    vec4 decalTex = texture(g_tDecal, coords);

    return (F_DECAL_BLEND_MODE == 0)
        ? mix(albedo, decalTex.rgb, decalTex.a)
        : albedo * decalTex.rgb;
}

// Bent normals
Sampler2D g_tBentNormal;

void GetBentNormal(inout MaterialProperties_t mat, vec2 texCoords)
{
    vec3 vBentNormalTs = DecodeHemiOctahedronNormal(texture(g_tBentNormal, texCoords).xy);
    mat.AmbientGeometricNormal = calculateWorldNormal(vBentNormalTs, mat.GeometricNormal, mat.Tangent, mat.Bitangent);
    mat.AmbientNormal = calculateWorldNormal(mat.NormalMap, mat.AmbientGeometricNormal, mat.Tangent, mat.Bitangent);
}

// Diffuse AO color bleed (skin/xen foliage)
[Default3(0.4, 0.14902, 0.129412)] [SrgbRead] uniform vec3 g_vAmbientOcclusionColorBleed;

void SetDiffuseColorBleed(inout MaterialProperties_t mat)
{
    vec3 vAmbientOcclusionExponent = vec3(1.0) - g_vAmbientOcclusionColorBleed.rgb;
    mat.DiffuseAO = pow(mat.DiffuseAO, vAmbientOcclusionExponent);
}

// Get material properties
MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals, VertexOutput vOut)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vOut.vFragPosition, vOut.vTangentOut, vOut.vBitangentOut);

    vec4 color = texture(g_tColor, texCoord);
    vec4 normalTexture = texture(g_tNormal, texCoord);

    float blendFactor = 0.0;

    // Blending
    if (terrain_blend_common)
    {
        vec2 texCoordB = texCoord * g_vTexCoordScale2.xy;

        vec4 color2;
        vec4 normalTexture2;

        if (vr_standard_vfx_blend)
        {
            if (F_SPECULAR == 1)
                color2 = texture(g_tLayer1Color, texCoordB);
            else
                color2 = texture(g_tLayer1Color2, texCoordB);
            normalTexture2 = normalTexture;
            if (F_BLEND_NORMALS == 1)
                normalTexture2 = texture(g_tLayer1Normal, texCoordB);
        }
        else
        {
            color2 = texture(g_tLayer2Color, texCoordB);
            normalTexture2 = texture(g_tLayer2NormalRoughness, texCoordB);
        }

        // Blend factor calculation
        if (csgo_generic_vfx_common && F_FANCY_BLENDING > 0)
        {
            blendFactor = vOut.vColorBlendValues.get().value.r;
            vec4 blendModTexel = texture(g_tBlendModulation, texCoordB);

            if (F_FANCY_BLENDING == 1)
                blendFactor = ApplyBlendModulation(blendFactor, blendModTexel.g, blendModTexel.r);
            else if (F_FANCY_BLENDING == 2)
                blendFactor = ApplyBlendModulation(blendFactor, blendModTexel.g, g_flBlendSoftness);
            else if (F_FANCY_BLENDING == 3)
                blendFactor = ApplyBlendModulation(blendFactor, blendModTexel.a, g_flBlendSoftness);
        }
        else if (steampal_2way_blend_mask_vfx != 0)
        {
            blendFactor = texture(g_tMask, texCoordB).x;
            blendFactor = ApplyBlendModulation(blendFactor, g_BlendFalloff, g_BlendHeight);
        }
        else if (simple_blend_common)
        {
            blendFactor = vOut.vColorBlendValues.get().value.r;
            vec4 blendModTexel = texture(g_tMask, texCoordB);

            float softnessPaint;
            if (csgo_simple_2way_blend_vfx != 0)
                softnessPaint = vOut.vColorBlendValues.get().value.a;
            else
                softnessPaint = vOut.vColorBlendValues.get().value.g;

            blendFactor = ApplyBlendModulation(blendFactor, blendModTexel.r, softnessPaint);
        }
        else if (vr_standard_vfx_blend)
        {
            blendFactor = vOut.vColorBlendValues.get().value.r;
            vec4 blendModTexel = texture(g_tLayer1RevealMask, texCoordB);
            blendFactor = ApplyBlendModulation(blendFactor, blendModTexel.g, blendModTexel.r * g_flLayer1BlendSoftness);
        }
        else if (environment_blend_vfx != 0)
        {
            blendFactor = vOut.vColorBlendValues.get().value.r;
            float revealMask = texture(g_tRevealMask2, texCoordB).r;
            blendFactor = ApplyBlendModulation(blendFactor, revealMask, g_flRevealSoftness2);
        }
        else
        {
            blendFactor = vOut.vColorBlendValues.get().value.r;
        }

        bool tintApplied = false;
        if (simple_blend_common && F_ENABLE_TINT_MASKS == 1)
        {
            tintApplied = true;
            vec2 tintMasks = texture(g_tTintMask, texCoord).xy;

            vec3 tintFactorA = 1.0 - tintMasks.x * (1.0 - vOut.vVertexColorOut.rgb);
            vec3 tintFactorB = 1.0 - tintMasks.y * (1.0 - vOut.vVertexColorOut.rgb);

            color.rgb *= tintFactorA;
            color2.rgb *= tintFactorB;
        }

        color = mix(color, color2, blendFactor);
        normalTexture = mix(normalTexture, normalTexture2, blendFactor);

        if (environment_blend_vfx != 0)
        {
            vec3 packed1 = texture(g_tPacked1, texCoord).rgb;
            vec3 packed2 = texture(g_tPacked2, texCoord).rgb;
            vec3 packedBlended = mix(packed1, packed2, blendFactor);
            mat.AmbientOcclusion = packedBlended.r;
            mat.Metalness = packedBlended.g;
            mat.Height = packedBlended.b;
        }
    }

    float flSelfIllumMask = 0.0;

    // Vr_skin unique stuff
    if (vr_skin_vfx != 0)
    {
        vec4 combinedMasks = texture(g_tCombinedMasks, texCoord);
        mat.ExtraParams.a = combinedMasks.x; // Mouth Mask
        mat.AmbientOcclusion = combinedMasks.y;

        if (F_SELF_ILLUM == 1)
            flSelfIllumMask = combinedMasks.z;

        if (isTranslucent || isAlphaTest)
            mat.Opacity = combinedMasks.a;
    }

    mat.Albedo = color.rgb;

    if (isTranslucent || isAlphaTest)
    {
        mat.Opacity = color.a;
    }
    else
    {
        // D_OPAQUE_FADE
        if (vOut.vVertexColorOut.a <= (254.0/255.0))
        {
            vec4 blueNoiseZeroToOne = texelFetch(g_tBlueNoise, ivec2(mod(gl_FragCoord.xy, textureSize(g_tBlueNoise, 0))), 0);
            if (vOut.vVertexColorOut.a <= blueNoiseZeroToOne.x)
            {
                discard;
            }
        }
    }

    if (static_overlay_vfx_common && (F_PAINT_VERTEX_COLORS == 1))
    {
        mat.Albedo *= vOut.vVertexColorOut.rgb;
        mat.Opacity *= vOut.vVertexColorOut.a;
    }

    if (isTranslucent)
    {
        mat.Opacity *= g_flOpacityScale;
    }

    // Alpha test
    if (isAlphaTest)
    {
        mat.Opacity = AlphaTestAntiAliasing(mat.Opacity, texCoord);
    }

    // Tinting
    bool tintApplied = (simple_blend_common && F_ENABLE_TINT_MASKS == 1);
    if (!tintApplied)
    {
        vec3 tintColor = vOut.vVertexColorOut.rgb;

        if (F_TINT_MASK == 1)
        {
            vec2 tintMaskTexcoord = texCoord;
            if ((F_SECONDARY_UV == 1) || (F_FORCE_UV2 == 1))
                tintMaskTexcoord = (g_bUseSecondaryUvForTintMask || (F_FORCE_UV2 == 1)) ? vOut.vTexCoord2.get().value : texCoord;

            float tintStrength = texture(g_tTintMask, tintMaskTexcoord).x;
            tintColor = 1.0 - tintStrength * (1.0 - tintColor.rgb);
        }

        mat.Albedo *= tintColor;
    }

    if (isSelfIllum)
    {
        // Standard mask sampling
        if (vr_skin_vfx == 0)
        {
            vec2 vSelfIllumMaskCoords = texCoord;

            if ((F_SECONDARY_UV == 1) || (F_FORCE_UV2 == 1))
                vSelfIllumMaskCoords = (g_bUseSecondaryUvForSelfIllum || (F_FORCE_UV2 == 1)) ? vOut.vTexCoord2.get().value : texCoord;

            vSelfIllumMaskCoords += fract(g_vSelfIllumScrollSpeed.xy * g_flTime);
            flSelfIllumMask = texture(g_tSelfIllumMask, vSelfIllumMaskCoords).r;
        }

        mat.IllumColor = GetStandardSelfIllumination(flSelfIllumMask, mat.Albedo);
    }

    if (isUnlit)
    {
        return mat;
    }

    if (vr_standard_vfx != 0 && (F_HIGH_QUALITY_GLOSS == 1))
    {
        normalTexture = texture(g_tNormal2, texCoord);
    }

    // Normals and Roughness
    if ((generic_vfx != 0) || (crystal_vfx != 0) || (vr_standard_vfx != 0) || (vr_eyeball_vfx != 0))
    {
        mat.NormalMap = DecodeDxt5Normal(normalTexture);
    }
    else
    {
        mat.NormalMap = DecodeHemiOctahedronNormal(normalTexture.rg);
    }

    bool anisoRoughness = (F_ANISOTROPIC_GLOSS == 1) || (vr_standard_vfx != 0 && F_HIGH_QUALITY_GLOSS == 1);

    if (anisoRoughness)
    {
        if (F_ANISOTROPIC_GLOSS == 1)
            mat.RoughnessTex.xy = texture(g_tAnisoGloss, texCoord).rg;
        if (vr_standard_vfx != 0 && F_HIGH_QUALITY_GLOSS == 1)
            mat.RoughnessTex.xy = texture(g_tGloss, texCoord).ag;
    }
    else
    {
        mat.RoughnessTex.xy = normalTexture.bb;
    }

    if (F_SCALE_NORMAL_MAP == 1)
    {
        mat.NormalMap = normalize(mix(vec3(0, 0, 1), mat.NormalMap, g_flNormalMapScaleFactor));
    }
    else if (csgo_generic_vfx_common)
    {
        mat.NormalMap = normalize(mix(vec3(0, 0, 1), mat.NormalMap, g_flBumpStrength));
    }

    // Detail texture
    if (F_DETAIL_TEXTURE > 0)
    {
        vec2 detailMaskCoords;
        if ((F_SECONDARY_UV == 1) || (F_FORCE_UV2 == 1))
            detailMaskCoords = (g_bUseSecondaryUvForDetailMask || (F_FORCE_UV2 == 1)) ? vOut.vTexCoord2.get().value : texCoord;
        else
            detailMaskCoords = texCoord;
        applyDetailTexture(mat.Albedo, mat.NormalMap, detailMaskCoords, vOut.vDetailTexCoords.get().value);
    }

    mat.Normal = calculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

    // Metalness
    if (_metalnessTexture)
    {
        vec4 metalnessTexture = texture(g_tMetalness, texCoord);
        mat.Metalness = metalnessTexture.g;

        if (F_RETRO_REFLECTIVE == 1)
            mat.ExtraParams.x = metalnessTexture.r;

        if (csgo_character_vfx != 0)
            mat.ClothMask = metalnessTexture.b * (1.0 - metalnessTexture.g);
        else if (csgo_weapon_vfx != 0)
            mat.RoughnessTex.xy = metalnessTexture.rr;
    }
    else if (_uniformMetalness)
    {
        mat.Metalness = g_flMetalness;
    }
    else if (_colorAlphaMetalness)
    {
        mat.Metalness = color.a;
    }
    else if (simple_blend_common)
    {
        mat.Metalness = mix(g_flMetalnessA, g_flMetalnessB, blendFactor);
    }
    else if (vr_standard_vfx != 0 && (F_METALNESS_TEXTURE == 1))
    {
        mat.Metalness = texture(g_tMetalnessReflectance, texCoord).r;
    }

    // Ambient Occlusion
    if (_colorAlphaAO)
    {
        mat.AmbientOcclusion = color.a;
    }
    else if (_ambientOcclusionTexture)
    {
        if ((F_SECONDARY_UV == 1) || (F_FORCE_UV2 == 1))
            mat.AmbientOcclusion = texture(g_tAmbientOcclusion, (g_bUseSecondaryUvForAmbientOcclusion || (F_FORCE_UV2 == 1)) ? vOut.vTexCoord2.get().value : texCoord).r;
        else
            mat.AmbientOcclusion = texture(g_tAmbientOcclusion, texCoord).r;
    }

    if (foliage_vfx_common)
    {
        vec4 vFoliageParams = clamp(vOut.vFoliageParamsOut.get().value, vec4(0.001), vec4(1.0));
        mat.AmbientOcclusion *= mix(1.0, pow(vFoliageParams.w, g_flVertexAmbientOcclusionPower), g_flVertexAmbientOcclusionAmount);
    }

    if (F_TRANSMISSIVE_BACKFACE_NDOTL == 1)
    {
        vec3 vTransmissiveColorSample = texture(g_tTransmissiveColor, texCoord).rgb;
        mat.TransmissiveColor = F_USE_ALBEDO_FOR_TRANSMISSIVE == 1 ? mat.Albedo : vTransmissiveColorSample;
    }

    if (vr_complex_vfx != 0 && F_METALNESS_TEXTURE == 0)
    {
        mat.ExtraParams.x = F_RETRO_REFLECTIVE == 1 ? g_flRetroReflectivity : 0.0;
    }

    if (vr_complex_vfx != 0)
    {
        mat.ClothMask = F_CLOTH_SHADING == 1 ? 1.0 : 0.0;
    }

    AdjustRoughnessByGeometricNormal(mat);

    if (csgo_character_vfx != 0 && F_EYEBALLS != 0)
    {
        ApplyEye(vOut.eyeInterpolator, texCoord, mat);
    }

    if (F_USE_BENT_NORMALS == 1)
    {
        GetBentNormal(mat, texCoord);
    }
    else
    {
        mat.AmbientNormal = mat.Normal;
        mat.AmbientGeometricNormal = mat.GeometricNormal;
    }

    if (vr_energy_field_vfx != 0)
    {
        mat.IllumColor = vec3(0.1, 0.3, 0.8);
        mat.Opacity = 0.2;
    }

    if (F_DECAL_TEXTURE == 1)
    {
        vec2 texCoord2Val = vec2(0);
        if ((F_SECONDARY_UV == 1) || (F_FORCE_UV2 == 1))
            texCoord2Val = vOut.vTexCoord2.get().value;
        mat.Albedo = ApplyDecalTexture(mat.Albedo, texCoord, texCoord2Val);
    }

    mat.DiffuseColor = mat.Albedo - mat.Albedo * mat.Metalness;

    vec3 F0 = vec3(0.04);

    if (csgo_weapon_vfx != 0)
    {
        F0 = vec3(0.02);
    }
    else if (csgo_character_vfx != 0)
    {
        if (F_CLOTH_SHADING == 1)
        {
            F0 = ApplySheen(0.04, mat.Albedo, mat.ClothMask);
        }
    }

    mat.SpecularColor = mix(F0, mat.Albedo, mat.Metalness);

    if (vr_skin_vfx != 0)
    {
        mat.TransmissiveColor = g_vTransmissionColor.rgb * color.a;

        float mouthOcclusion = mix(1.0, g_flMouthInteriorBrightnessScale, mat.ExtraParams.a);
        mat.TransmissiveColor *= mouthOcclusion;
        mat.AmbientOcclusion *= mouthOcclusion;
    }

    if ((F_GLASS == 1) || (vr_glass_vfx != 0))
    {
        vec4 glassResult = GetGlassMaterial(mat);
        mat.Albedo = glassResult.rgb;
        mat.Opacity = glassResult.a;
    }

    if (csgo_glass_vfx != 0)
    {
        mat.Opacity = mix(g_flTranslucencyRemap.x, g_flTranslucencyRemap.y, mat.Opacity);
    }

    mat.DiffuseAO = vec3(mat.AmbientOcclusion);
    mat.SpecularAO = mat.AmbientOcclusion;

    if (anisoRoughness)
    {
        CalculateAnisotropicTangents(mat);
    }

    return mat;
}

// MAIN

[shader("fragment")]
float4 fragmentMain(VertexOutput vOut) : SV_Target
{
    vOut.sFeatureData.MakeGlobal();

    vFragPosition = vOut.vFragPosition;

    if (hasAnisoBitangent)
    {
        pbrAnisoBitangent = vOut.vAnisoBitangentOut.get().value;
    }

    vec3 vertexNormal = SwitchCentroidNormal(vOut.vNormalOut, vOut.vCentroidNormalOut);
    vec2 texCoord = vOut.vTexCoordOut;

    // Get material
    MaterialProperties_t mat = GetMaterial(texCoord, vertexNormal, vOut);
    float4 outputColor = float4(1);
    outputColor.a = mat.Opacity;

    LightingTerms_t lighting = InitLighting();

    if (isUnlit)
    {
        lighting.DiffuseDirect = mat.Albedo + mat.IllumColor;
        outputColor.rgb = lighting.DiffuseDirect;
    }
    else
    {
        lighting = CalculateLighting(mat);
        ApplyAmbientOcclusion(lighting, mat);

        vec3 diffuseLighting = lighting.DiffuseDirect + lighting.DiffuseIndirect;
        vec3 specularLighting = lighting.SpecularDirect + lighting.SpecularIndirect;

        if (F_NO_SPECULAR_AT_FULL_ROUGHNESS == 1)
        {
            specularLighting = (mat.Roughness == vec2(1.0)) ? vec3(0) : specularLighting;
        }

        // Unique HLA Membrane blend mode
        if (vr_complex_vfx != 0 && F_TRANSLUCENT == 2)
        {
            vec3 combinedLighting = specularLighting + (mat.DiffuseColor * diffuseLighting + lighting.TransmissiveDirect + mat.IllumColor) * mat.Opacity;
            outputColor.a = 1.0;
            outputColor.rgb = combinedLighting;
        }
        else
        {
            vec3 combinedLighting = mat.DiffuseColor * diffuseLighting + specularLighting + lighting.TransmissiveDirect + mat.IllumColor;
            outputColor.rgb = combinedLighting;
        }
    }

    ApplyFog(outputColor.rgb, mat.PositionWS);

    if (F_DISABLE_TONE_MAPPING == 1)
    {
        outputColor.rgb = SrgbGammaToLinear(outputColor.rgb);
    }

    if (isBlendMod2x || isBlendModThenAdd)
    {
        vec3 gammaOutput = SrgbLinearToGamma(outputColor.rgb);
        if (isBlendMod2x)
        {
            outputColor = vec4(mix(vec3(0.5), gammaOutput, vec3(outputColor.a)), outputColor.a);
        }
        else if (isBlendModThenAdd)
        {
            outputColor = vec4(mix(vec3(0), outputColor.rgb, vec3(0.5)), outputColor.a);
        }
    }

    if (HandleMaterialRenderModes(outputColor, mat)
    || HandleLightingRenderModes(outputColor, mat, lighting)
    || HandleUVRenderModes(outputColor, mat, g_tColor, vOut.vTexCoordOut.xy))
    {
        //
    }
    else if (g_iRenderMode == renderMode_Cubemaps)
    {
        // No bumpmaps, full reflectivity
        vec3 viewmodeEnvMap = GetEnvironmentNoBRDF(mat, 0.2).rgb;
        outputColor.rgb = SrgbLinearToGamma(viewmodeEnvMap);
    }
    else if (g_iRenderMode == renderMode_Tint)
    {
        outputColor = vec4(vOut.vVertexColorOut.rgb, vOut.vVertexColorOut.a);
    }
    else if (foliage_vfx_common && g_iRenderMode == renderMode_FoliageParams)
    {
        outputColor.rgb = vOut.vFoliageParamsOut.get().value.rgb;
    }
    else if (terrain_blend_common && (steampal_2way_blend_mask_vfx == 0) && g_iRenderMode == renderMode_TerrainBlend)
    {
        outputColor.rgb = vOut.vColorBlendValues.get().value.rgb;
    }

    return outputColor;
}
