#version 460

import common.utils;
import common.attributes;
import common.features;
import common.ViewConstants;
import common.LightingConstants;
import "complex_features.slang";

import "common/instancing.slang";
import common.animation;
import common.morph;
import common.compression;

import features.csgo_character_eyes_common;
import features.csgo_character_eyes_vs;

//Parameter defines - These are default values and can be overwritten based on material/model parameters
public extern static const int F_NOTINT = 0;
public extern static const int F_VERTEX_COLOR = 0;
public extern static const int F_TEXTURE_ANIMATION = 0;
public uniform int F_TEXTURE_ANIMATION_MODE;
// Tex coord uniforms
[Default3(1.0, 1.0, 1.0)] [SrgbRead] uniform vec3 g_vColorTint;
[Default(1.0)] uniform float g_flModelTintAmount;
[Default(1.0)] uniform float g_flFadeExponent;

uniform vec2 g_vTexCoordOffset;
[Default2(1.0, 1.0)] uniform vec2 g_vTexCoordScale;
uniform vec2 g_vTexCoordScrollSpeed;
[Default2(0.5, 0.5)] uniform vec2 g_vTexCoordCenter;
uniform float g_flTexCoordRotation;

// Texture animation
[Default2(1.0, 1.0)] uniform vec2 g_vAnimationGrid;
[Default(1)] uniform int g_nNumAnimationCells;
uniform float g_flAnimationTimePerFrame;
uniform float g_flAnimationTimeOffset;
uniform float g_flAnimationFrame;

// Foliage animation
[Default(0.25)] uniform float g_flSwayAmount;
[Default(1.0)] uniform float g_flSwaySpeed;
[Default(1.0)] uniform float g_flSwayNoiseScale;
[Default(1.0)] uniform float g_flSwayFalloff;
[Default(0.25)] uniform float g_flFlutterAmount;
[Default(1.0)] uniform float g_flFlutterSpeed;
[Default(1.0)] uniform float g_flFlutterNoiseScale;
[Default(1.0)] uniform float g_flFlutterFalloff;
uniform sampler3D g_tNoiseMap;

const vec4 g_vWindDirection = vec4(0.0, -1.0, 0.0, 0.0);
const vec4 g_vWindStrengthFreqMulHighStrength = vec4(0.5);

struct FoliageAnimParams
{
    float swayStrength;
    float flutterStrength;
    float swayFalloff;
    float flutterFalloff;
    vec3 swayTimeOffset;
    vec3 flutterTimeOffset;
    vec3 swayNoiseScale;
    vec3 flutterNoiseScale;
    float heightBlend;
    vec3 heightOffset;
};

vec3 vertexAnimation(vec3 pos, FoliageAnimParams params)
{
    vec3 swayNoisePos = ((pos + params.heightOffset) * params.swayNoiseScale) + params.swayTimeOffset;
    float swayNoiseMask = clamp(pow(textureLod(g_tNoiseMap, swayNoisePos * vec3(0.4, 0.4, 0.125), 0.0).z, 2.0) * 1.5, 0.0, 1.0);
    vec3 swayNoise = (textureLod(g_tNoiseMap, swayNoisePos, 0.0).xyz - vec3(0.5)) * 2.0;
    vec3 swayDir = vec3(swayNoise.xy, swayNoise.y * 0.5) * 0.5;

    vec3 swayOffset = ((swayDir * swayNoiseMask + g_vWindDirection.xyz) * params.swayFalloff *
                    g_vWindStrengthFreqMulHighStrength.x * params.swayStrength) * swayNoiseMask;

    vec3 flutterNoisePos = ((pos + vec3(params.heightBlend * 10.0, params.heightBlend * 5.0, params.heightBlend * 5.0)) *
                        params.flutterNoiseScale) + params.flutterTimeOffset;
    vec3 flutterNoise = (textureLod(g_tNoiseMap, flutterNoisePos, 0.0).xyz - vec3(0.5)) * 2.0;

    vec3 flutterOffset = ((flutterNoise * g_vWindDirection.xyz + flutterNoise * swayDir) *
                        g_vWindStrengthFreqMulHighStrength.x * params.flutterStrength) * params.flutterFalloff;

    vec3 combinedOffset = swayOffset + flutterOffset;
    vec3 gravityComp = vec3(0.0, 0.0, -length(combinedOffset.xy) * 0.314);
    return pos + combinedOffset + gravityComp;
}

// Spherical projected anisotropic tangents
[Default(180.0)] uniform float g_vSphericalAnisotropyAngle;
[Default3(0.0, 0.0, 1.0)] uniform vec3 g_vSphericalAnisotropyPole;

vec3 GetSphericalProjectedAnisoBitangent(vec3 normal, vec3 tangent)
{
    float angle = (g_vSphericalAnisotropyAngle / 90.0) - 1.0;

    vec3 vAnisoTangent = cross(g_vSphericalAnisotropyPole.xyz, normal);
    vAnisoTangent = mix(vAnisoTangent, tangent, bvec3(length(vec3(equal(vAnisoTangent, vec3(0.0)))) != 0.0));

    if (g_vSphericalAnisotropyAngle != 0.0)
    {
        vec3 rotated1 = mix(cross(normal, vAnisoTangent), vAnisoTangent, ClampToPositive(g_vSphericalAnisotropyAngle));
        vAnisoTangent = mix(rotated1, -vAnisoTangent, ClampToPositive(-g_vSphericalAnisotropyAngle));
    }
    return vAnisoTangent;
}

vec2 GetAnimatedUVs(vec2 texCoords)
{
    if (F_TEXTURE_ANIMATION == 1)
    {
        uint frame = uint(g_flAnimationFrame);
        uint cells = uint(g_nNumAnimationCells);
        if (F_TEXTURE_ANIMATION_MODE == 0) // Sequential
            frame = uint((g_flAnimationTimeOffset + g_flTime) / g_flAnimationTimePerFrame) % cells;
        else if (F_TEXTURE_ANIMATION_MODE == 1) // Random
            frame = uint(Random2D(vec2(g_flAnimationFrame)) * float(g_nNumAnimationCells));

        vec2 atlasGridInv = vec2(1.0) / g_vAnimationGrid.xy;
        vec2 atlasOffset = vec2(uvec2(
            frame % uint(g_vAnimationGrid.x),
            uint(float(frame) * atlasGridInv.x)
        )) * atlasGridInv;

        texCoords = texCoords * atlasGridInv + atlasOffset;
    }

    texCoords = RotateVector2D(texCoords, g_flTexCoordRotation, g_vTexCoordScale.xy, g_vTexCoordOffset.xy, g_vTexCoordCenter.xy);
    return texCoords + g_vTexCoordScrollSpeed.xy * g_flTime;
}

vec4 GetTintColorLinear(vec4 vTint)
{
    vec4 TintFade = vec4(1.0);
    if (F_NOTINT == 0)
    {
        TintFade.rgb = mix(vec3(1.0), SrgbGammaToLinear(vTint.rgb) * g_vColorTint.rgb, g_flModelTintAmount);
    }
    TintFade.a = pow(vTint.a, g_flFadeExponent);
    return TintFade;
}

// Detail texture coord uniforms
uniform float g_flDetailTexCoordRotation;
uniform vec2 g_vDetailTexCoordOffset;
[Default2(1.0, 1.0)] uniform vec2 g_vDetailTexCoordScale;
uniform bool g_bUseSecondaryUvForDetailTexture;

// Intermediate booleans for Conditional template arguments (must be derived directly from externs to be compile-time constant)
public static const bool hasFoliage = (csgo_foliage_vfx != 0) || (vr_complex_vfx != 0 && F_FOLIAGE_ANIMATION > 0);
public static const bool hasSecondaryUV = (F_SECONDARY_UV == 1) || (F_FORCE_UV2 == 1);
public static const bool hasBlendInputs = (F_LAYERS > 0) || (vr_simple_2way_blend_vfx != 0) || (csgo_simple_2way_blend_vfx != 0) || (vr_simple_2way_parallax_vfx != 0) || (vr_simple_3way_parallax_vfx != 0) || (vr_simple_blend_to_triplanar_vfx != 0) || (vr_simple_blend_to_xen_membrane_vfx != 0) || (vr_standard_vfx != 0 && F_BLEND > 0) || (environment_blend_vfx != 0);
public static const bool hasDetailTexture = (F_DETAIL_TEXTURE > 0);
public static const bool hasAnisoBitangent = (F_SPHERICAL_PROJECTED_ANISOTROPIC_TANGENTS == 1);
public static const bool hasEyeballs = (csgo_character_vfx != 0) && (F_EYEBALLS != 0);
public static const bool hasNonFoliageVertexColor = (csgo_foliage_vfx == 0) && (vr_complex_vfx == 0 || F_FOLIAGE_ANIMATION == 0) && ((F_VERTEX_COLOR == 1) || (F_PAINT_VERTEX_COLORS == 1));
public static const bool hasVrStandardVertexStream = (vr_standard_vfx != 0) && (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1);
public static const bool hasVrBlend = (vr_simple_2way_blend_vfx != 0) || (vr_simple_2way_parallax_vfx != 0) || (vr_simple_3way_parallax_vfx != 0) || (vr_simple_blend_to_triplanar_vfx != 0) || (vr_simple_blend_to_xen_membrane_vfx != 0);

// VertexOutput struct shared between vertex and fragment shaders
public struct VertexOutput
{
    vec4 vRasterPosition : SV_Position;
    vec3 vFragPosition;
    vec3 vNormalOut;
    vec3 vTangentOut;
    vec3 vBitangentOut;
    vec2 vTexCoordOut;
    vec4 vVertexColorOut;
    vec3 vCentroidNormalOut;

    FeatureData sFeatureData;

    Conditional<float3, D_BAKED_LIGHTING_FROM_LIGHTMAP == 1> vLightmapUVScaled;
    Conditional<float3, D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1> vPerVertexLightingOut;
    Conditional<float4, hasFoliage> vFoliageParamsOut;
    Conditional<float2, hasSecondaryUV> vTexCoord2;
    Conditional<float4, hasBlendInputs> vColorBlendValues;
    Conditional<float2, hasDetailTexture> vDetailTexCoords;
    Conditional<float3, hasAnisoBitangent> vAnisoBitangentOut;
    EyePixelInput eyeInterpolator;
};

[shader("vertex")]
VertexOutput vertexMain(
    vec3 vPOSITION,
    vec2 vTEXCOORD,
    vec4 vNORMAL,
    vec4 vTANGENT,
    uvec4 vBLENDINDICES,
    vec4 vBLENDWEIGHT,
    Conditional<float2, D_BAKED_LIGHTING_FROM_LIGHTMAP == 1> vLightmapUV,
    Conditional<float4, D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1> vPerVertexLighting,
    Conditional<float4, hasFoliage> vFoliageParams,
    Conditional<float4, hasSecondaryUV> vTEXCOORD1,
    Conditional<float4, hasNonFoliageVertexColor> vCOLOR,
    Conditional<float4, hasBlendInputs> vBLEND_COLOR,
    Conditional<float4, hasVrBlend> vBLEND_ALPHA,
    Conditional<float4, hasVrStandardVertexStream> vCOLOR1,
    uint vVertexID : SV_VertexID
)
{
    // Set up animation inputs
    AnimationInput animInput;
    animInput.blendIndices = vBLENDINDICES;
    animInput.blendWeight = vBLENDWEIGHT;

    // Set up compression inputs
    inCompressedNormal = reinterpret<uint, float4>(vNORMAL);
    inNormal = vNORMAL;
    inTangent = vTANGENT;

    // Set up morph input
    if (F_MORPH_SUPPORTED == 1)
        vertexID = vVertexID;

    VertexOutput vOut;

    ObjectData_t object = GetObjectData();
    mat4 skinTransform = object.transform * getSkinMatrix(animInput);
    vOut.vFragPosition = (skinTransform * vec4(vPOSITION + getMorphOffset(), 1.0)).xyz;

    vec3 normal;
    vec4 tangent;
    GetOptionallyCompressedNormalTangent(normal, tangent);

    mat3 normalTransform = adjoint(skinTransform);
    vOut.vNormalOut = normalize(normalTransform * normal);
    vOut.vTangentOut = normalize(normalTransform * tangent.xyz);
    vOut.vBitangentOut = tangent.w * cross(vOut.vNormalOut, vOut.vTangentOut);

    if (hasFoliage)
    {
        vec4 foliageParamsVal = vFoliageParams.get().value;
        vOut.vFoliageParamsOut.set(foliageParamsVal);

        vec4 foliageParams = clamp(foliageParamsVal, vec4(0.001), vec4(1.0));

        FoliageAnimParams animParams;
        animParams.swayStrength = 150.0 * g_flSwayAmount;
        animParams.flutterStrength = 25.0 * g_flFlutterAmount;
        animParams.swayFalloff = pow(foliageParams.x, g_flSwayFalloff);
        animParams.flutterFalloff = pow(foliageParams.z, g_flFlutterFalloff);
        animParams.heightBlend = (2.0 * foliageParams.y) - 1.0;
        animParams.heightOffset = vec3(animParams.heightBlend * 100.0, 0.0, animParams.heightBlend * 50.0);
        animParams.swayTimeOffset = vec3(-0.3, 0.0, -0.03) * g_flSwaySpeed * g_flTime;
        animParams.flutterTimeOffset = vec3(-0.35, 0.0, 0.1) * g_flFlutterSpeed * g_flTime;
        animParams.swayNoiseScale = vec3(0.00075, 0.00075, 0.00005) * g_flSwayNoiseScale;
        animParams.flutterNoiseScale = vec3(0.0025, 0.0025, 0.0005) * g_flFlutterNoiseScale;

        vOut.vFragPosition = vertexAnimation(vOut.vFragPosition.xyz, animParams);
    }

    vOut.vRasterPosition = g_matWorldToProjection * vec4(vOut.vFragPosition, 1.0);

    if (hasAnisoBitangent)
    {
        vOut.vAnisoBitangentOut.set(normalTransform * GetSphericalProjectedAnisoBitangent(normal, tangent.xyz));
    }

    if (hasEyeballs)
    {
        vOut.eyeInterpolator = GetCharacterEyeInterpolator(vPOSITION);
    }

    vOut.vTexCoordOut = GetAnimatedUVs(vTEXCOORD.xy);

    if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    {
        vOut.vLightmapUVScaled.set(vec3(vLightmapUV.get().value * g_vLightmapUvScale.xy, 0));
    }
    else if (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
    {
        vec4 vertexLight;
        if (vr_standard_vfx != 0)
            vertexLight = vCOLOR1.get().value;
        else
            vertexLight = vPerVertexLighting.get().value;

        vec3 Light = vertexLight.rgb * 6.0 * vertexLight.a;
        vOut.vPerVertexLightingOut.set(pow2(Light));
    }

    vOut.sFeatureData.vLightmapUVScaled.set(vOut.vLightmapUVScaled.get().value);
    vOut.sFeatureData.vPerVertexLightingOut.set(vOut.vPerVertexLightingOut.get().value);

    vOut.vVertexColorOut = GetTintColorLinear(object.vTint);

    if (hasNonFoliageVertexColor)
    {
        vOut.vVertexColorOut *= SrgbGammaToLinear(vCOLOR.get().value);
    }

    if (hasSecondaryUV)
    {
        vOut.vTexCoord2.set(vTEXCOORD1.get().value.xy);
    }

    if (hasDetailTexture)
    {
        vec2 detailCoords;
        if (hasSecondaryUV)
            detailCoords = (g_bUseSecondaryUvForDetailTexture || (F_FORCE_UV2 == 1)) ? vOut.vTexCoord2.get().value : vOut.vTexCoordOut;
        else
            detailCoords = vOut.vTexCoordOut;
        vOut.vDetailTexCoords.set(RotateVector2D(detailCoords, g_flDetailTexCoordRotation, g_vDetailTexCoordScale.xy, g_vDetailTexCoordOffset.xy));
    }

    if (hasBlendInputs)
    {
        vec4 blendColor = vBLEND_COLOR.get().value;

        if (hasVrBlend)
        {
            blendColor.y = max(0.5 * vBLEND_ALPHA.get().value.x, 0.1);
        }

        vOut.vColorBlendValues.set(blendColor);
    }

    vOut.vCentroidNormalOut = vOut.vNormalOut;

    return vOut;
}
