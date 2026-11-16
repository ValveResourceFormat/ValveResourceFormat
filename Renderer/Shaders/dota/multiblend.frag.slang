#version 460

// Render modes -- Switched on/off by code
#define renderMode_FullBright 0
#define renderMode_Color 0
#define renderMode_Normals 0
#define renderMode_Roughness 0
#define renderMode_Tangents 0
#define renderMode_BumpMap 0
#define renderMode_BumpNormals 0
#define renderMode_Illumination 0
#define renderMode_TerrainBlend 0
#define renderMode_Tint 0

//Parameter defines - These are default values and can be overwritten based on material/model parameters
#define F_TINT_MASK 0
#define F_TINT_MASK_2 0
#define F_NORMAL_MAP 0
#define F_SPECULAR 0
#define F_GLOBAL_TINT 0
#define F_LAYER_BORDER_TINT 0
#define F_PARALLAX_MAP_ON_0 0
#define F_PARALLAX_MAP_ON_3 0
//End of parameter defines

in vec3 vFragPosition;

in vec3 vNormalOut;
in vec4 vTangentOut;

in vec4 vBlendWeights;
in vec4 vBlendAlphas;
in vec4 vVertexColor;

in vec2 vTexCoordOut;
in vec2 vTexCoord1Out;
in vec2 vTexCoord2Out;
in vec2 vTexCoord3Out;

out vec4 outputColor;

uniform sampler2D g_tColor0; // SrgbRead(true)
uniform sampler2D g_tColor1; // SrgbRead(true)
uniform sampler2D g_tColor2; // SrgbRead(true)
uniform sampler2D g_tColor3; // SrgbRead(true)

uniform sampler2D g_tNormal0;
uniform sampler2D g_tNormal1;
uniform sampler2D g_tNormal2;
uniform sampler2D g_tNormal3;

#if (F_SPECULAR == 1)
    uniform sampler2D g_tSpecular0; // SrgbRead(true)
    uniform sampler2D g_tSpecular1; // SrgbRead(true)
    uniform sampler2D g_tSpecular2; // SrgbRead(true)
    uniform sampler2D g_tSpecular3; // SrgbRead(true)
#endif

#if (F_TINT_MASK == 1)
    uniform sampler2D g_tTintMasks;
#endif

#if (F_TINT_MASK_2 == 1)
    uniform sampler2D g_tTint2Masks;
#endif

uniform vec3 g_vColorTintC0 = vec3(1.0); // SrgbRead(true)
uniform vec3 g_vColorTintC1 = vec3(1.0); // SrgbRead(true)
uniform vec3 g_vColorTintC2 = vec3(1.0); // SrgbRead(true)
uniform vec3 g_vColorTintC3 = vec3(1.0); // SrgbRead(true)
uniform vec3 g_vColorTintD0 = vec3(1.0); // SrgbRead(true)
uniform vec3 g_vColorTintD1 = vec3(1.0); // SrgbRead(true)
uniform vec3 g_vColorTintD2 = vec3(1.0); // SrgbRead(true)
uniform vec3 g_vColorTintD3 = vec3(1.0); // SrgbRead(true)

#if (F_PARALLAX_MAP_ON_0 == 1 || F_PARALLAX_MAP_ON_3 == 1)
    uniform sampler2D g_tParallaxMask;
#endif

#if (F_PARALLAX_MAP_ON_0 == 1)
    uniform float g_flParallaxStrength0 = 0.1;
#endif

#if (F_PARALLAX_MAP_ON_3 == 1)
    uniform float g_flParallaxStrength3 = 0.1;
#endif

#if (F_LAYER_BORDER_TINT == 2)
    uniform vec3 g_vBorderColorTint1 = vec3(1.0); // SrgbRead(true)
    uniform vec3 g_vBorderColorTint2 = vec3(1.0); // SrgbRead(true)
    uniform vec3 g_vBorderColorTint3 = vec3(1.0); // SrgbRead(true)
    uniform float g_flBorderOffset1;
    uniform float g_flBorderOffset2;
    uniform float g_flBorderOffset3;
    uniform float g_flBorderTintStrength1 = 1.0;
    uniform float g_flBorderTintStrength2 = 1.0;
    uniform float g_flBorderTintStrength3 = 1.0;
    uniform float g_flBorderTintSoftness1 = 0.5;
    uniform float g_flBorderTintSoftness2 = 0.5;
    uniform float g_flBorderTintSoftness3 = 0.5;
#endif

#if (F_GLOBAL_TINT == 1)
    uniform vec4 g_vGlobalTint = vec4(1.0); // SrgbRead(true)
#endif

uniform vec3 g_vColorTint0 = vec3(1.0); // SrgbRead(true)
uniform vec3 g_vColorTint1 = vec3(1.0); // SrgbRead(true)
uniform vec3 g_vColorTint2 = vec3(1.0); // SrgbRead(true)
uniform vec3 g_vColorTint3 = vec3(1.0); // SrgbRead(true)
uniform vec3 g_vColorTintB0 = vec3(1.0); // SrgbRead(true)
uniform vec3 g_vColorTintB1 = vec3(1.0); // SrgbRead(true)
uniform vec3 g_vColorTintB2 = vec3(1.0); // SrgbRead(true)
uniform vec3 g_vColorTintB3 = vec3(1.0); // SrgbRead(true)

// Layer texcoord rotation in degrees and scale, in the same units as the vertex shader
uniform float g_flTexCoordRotate0;
uniform float g_flTexCoordRotate1;
uniform float g_flTexCoordRotate2;
uniform float g_flTexCoordRotate3;
uniform float g_flTexCoordScale0 = 1.0;
uniform float g_flTexCoordScale3 = 1.0;

#include "../common/utils.slang"
#include "../common/view_constants.slang"

uniform float g_flBumpStrength = 1.0;
uniform float g_flSpecularIntensity = 1.0;

#include "../common/lighting_constants.slang"
#include "../common/lighting_common.slang"
#include "../common/shadowmapping.slang"
#include "../common/fog.slang"
#include "global_light.slang"

// One value per layer
struct Layers_t
{
    vec4 Layer0;
    vec4 Layer1;
    vec4 Layer2;
    vec4 Layer3;
};

struct LayerUvs_t
{
    vec2 Layer0;
    vec2 Layer1;
    vec2 Layer2;
    vec2 Layer3;
};

#if (F_PARALLAX_MAP_ON_0 == 1 || F_PARALLAX_MAP_ON_3 == 1)
// Parallax shift of a layer's texcoords, in the layer's rotated and scaled texcoord space
vec2 GetParallaxOffset(vec2 uv, vec2 viewTs, int channel, float strength, float rotation, float scale)
{
    float mask = texture(g_tParallaxMask, uv)[channel];
    return RotateVector2D(viewTs * mask * strength, rotation) / scale;
}
#endif

LayerUvs_t GetLayerUvs()
{
    LayerUvs_t uvs;
    uvs.Layer0 = vTexCoordOut;
    uvs.Layer1 = vTexCoord1Out;
    uvs.Layer2 = vTexCoord2Out;
    uvs.Layer3 = vTexCoord3Out;

#if (F_PARALLAX_MAP_ON_0 == 1 || F_PARALLAX_MAP_ON_3 == 1)
    vec3 viewDirection = normalize(vFragPosition - g_vCameraPositionWs);
    vec3 tangent = normalize(vTangentOut.xyz);
    vec3 bitangent = normalize(GetBitangent(vNormalOut, vTangentOut));
    vec2 viewTs = vec2(dot(viewDirection, tangent), dot(viewDirection, bitangent));
#endif

#if (F_PARALLAX_MAP_ON_0 == 1)
    uvs.Layer0 += GetParallaxOffset(vTexCoordOut, viewTs, 0, g_flParallaxStrength0, g_flTexCoordRotate0, g_flTexCoordScale0);
#endif

#if (F_PARALLAX_MAP_ON_3 == 1)
    uvs.Layer3 += GetParallaxOffset(vTexCoord3Out, viewTs, 3, g_flParallaxStrength3, g_flTexCoordRotate3, g_flTexCoordScale3);
#endif

    return uvs;
}

Layers_t SampleLayers(sampler2D texture0, sampler2D texture1, sampler2D texture2, sampler2D texture3, LayerUvs_t uvs)
{
    Layers_t layers;
    layers.Layer0 = texture(texture0, uvs.Layer0);
    layers.Layer1 = texture(texture1, uvs.Layer1);
    layers.Layer2 = texture(texture2, uvs.Layer2);
    layers.Layer3 = texture(texture3, uvs.Layer3);
    return layers;
}

vec4 BlendLayers(vec4 weights, Layers_t layers)
{
    return weights.x * layers.Layer0 + weights.y * layers.Layer1 + weights.z * layers.Layer2 + weights.w * layers.Layer3;
}

// Each layer's weight in a vec4, taking what the layers above leave over
vec4 GetBlendWeights(Layers_t colors)
{
    float blendWeight1 = ApplyBlendModulation(vBlendWeights.x, colors.Layer1.w, vBlendAlphas.x);

    float invBlendWeight1 = 1.0 - blendWeight1;

    float blendWeight2 = ApplyBlendModulation(vBlendWeights.y, colors.Layer2.w, vBlendAlphas.y);
    blendWeight2 = clamp(blendWeight2, 0.0, invBlendWeight1);

    float invBlendWeight2 = invBlendWeight1 - blendWeight2;

    float blendWeight3 = ApplyBlendModulation(vBlendWeights.z, colors.Layer3.w, vBlendAlphas.z);
    blendWeight3 = clamp(blendWeight3, 0.0, invBlendWeight2);

    // remaining weight
    float blendWeight0 = invBlendWeight2 - blendWeight3;

    return vec4(blendWeight0, blendWeight1, blendWeight2, blendWeight3);
}

// Interpolate between the tint colors based on the tint masks
vec3 GetLayerTint(int id, vec3 tintBase, vec3 tint, vec3 tintC, vec3 tintD, vec2 coords)
{
#if (F_TINT_MASK == 1)
    float maskValue = texture(g_tTintMasks, coords)[id];
    vec3 layerTint = mix(tintBase, tint, maskValue);

#if (F_TINT_MASK_2 == 1)
    float mask2 = texture(g_tTint2Masks, coords)[id];
    return ApplyTintMask2(layerTint, mask2, tintC, tintD);
#else
    return layerTint;
#endif
#else
    return tint;
#endif
}

#if (F_LAYER_BORDER_TINT == 2)
// Tints the band of a layer's reveal mask around the vertex amount
vec3 GetBorderTint(vec3 borderColor, float softnessParam, float offset, float strength, float vertexAmount, float revealMask)
{
    float softness = max(0.01, softnessParam * 2.0);
    float width = softness * 2.0;
    float edge = saturate(vertexAmount - offset) * (1.0 + width) - softness - softness;
    float distanceToEdge = 1.0 - saturate((revealMask - edge) / width);
    float amount = (1.0 - abs(distanceToEdge * 2.0 - 1.0)) * strength;

    return mix(vec3(1.0), borderColor, amount);
}

vec3 GetBorderTints(vec4 weights, Layers_t colors)
{
    vec3 border1 = GetBorderTint(g_vBorderColorTint1, g_flBorderTintSoftness1, g_flBorderOffset1, g_flBorderTintStrength1, vBlendWeights.x, colors.Layer1.w);
    vec3 border2 = GetBorderTint(g_vBorderColorTint2, g_flBorderTintSoftness2, g_flBorderOffset2, g_flBorderTintStrength2, vBlendWeights.y, colors.Layer2.w);
    vec3 border3 = GetBorderTint(g_vBorderColorTint3, g_flBorderTintSoftness3, g_flBorderOffset3, g_flBorderTintStrength3, vBlendWeights.z, colors.Layer3.w);

    // Each layer hides the borders of the layers below it
    vec3 borders = mix(border3, vec3(1.0), weights.w) * border2;
    borders = mix(borders, vec3(1.0), weights.z) * border1;
    return mix(borders, vec3(1.0), weights.y);
}
#endif

vec3 BlendColors(vec4 weights, Layers_t colors, LayerUvs_t uvs)
{
    vec3 tint0 = GetLayerTint(0, g_vColorTintB0, g_vColorTint0, g_vColorTintC0, g_vColorTintD0, uvs.Layer0);
    vec3 tint1 = GetLayerTint(1, g_vColorTintB1, g_vColorTint1, g_vColorTintC1, g_vColorTintD1, uvs.Layer1);
    vec3 tint2 = GetLayerTint(2, g_vColorTintB2, g_vColorTint2, g_vColorTintC2, g_vColorTintD2, uvs.Layer2);
    vec3 tint3 = GetLayerTint(3, g_vColorTintB3, g_vColorTint3, g_vColorTintC3, g_vColorTintD3, uvs.Layer3);

    vec3 blended = weights.x * colors.Layer0.rgb * tint0
        + weights.y * colors.Layer1.rgb * tint1
        + weights.z * colors.Layer2.rgb * tint2
        + weights.w * colors.Layer3.rgb * tint3;

#if (F_LAYER_BORDER_TINT == 2)
    blended *= GetBorderTints(weights, colors);
#endif

    blended *= vVertexColor.rgb;

#if (F_GLOBAL_TINT == 1)
    blended *= g_vGlobalTint.rgb;
#endif

    return blended;
}

// The packed normal of a layer, rotated around the texture center to follow the layer's texcoord rotation
vec2 GetRotatedNormal(vec4 normalTexel, float rotation)
{
    return RotateVector2D(normalTexel.ag - 0.5, rotation) + 0.5;
}

// Tangent space normal of the blended normal maps
vec3 GetBumpNormalTs(vec4 weights, LayerUvs_t uvs)
{
#if (F_NORMAL_MAP == 1)
    vec2 blendedNormal = weights.x * GetRotatedNormal(texture(g_tNormal0, uvs.Layer0), g_flTexCoordRotate0)
        + weights.y * GetRotatedNormal(texture(g_tNormal1, uvs.Layer1), g_flTexCoordRotate1)
        + weights.z * GetRotatedNormal(texture(g_tNormal2, uvs.Layer2), g_flTexCoordRotate2)
        + weights.w * GetRotatedNormal(texture(g_tNormal3, uvs.Layer3), g_flTexCoordRotate3);

    vec3 bumpNormalTs = DecodeDxt5Normal(vec4(0.0, blendedNormal.y, 0.0, blendedNormal.x));
    bumpNormalTs.xy *= g_flBumpStrength;

    return bumpNormalTs;
#else
    return vec3(0.0, 0.0, 1.0);
#endif
}

vec3 CalculateWorldNormal(vec3 normalTs)
{
#if (F_NORMAL_MAP == 1)
    return CalculateNormalizedWorldNormal(normalTs);
#else
    return normalize(vNormalOut.xyz);
#endif
}

vec3 GetIllumination(vec3 worldNormal, out float sunLit)
{
    sunLit = 0.0;

    if (g_iRenderMode == renderMode_FullBright)
    {
        return vec3(1.0);
    }

    return CalculateDotaGlobalLight(worldNormal, vFragPosition, sunLit);
}

vec4 ApplyRenderMode(vec4 litColor, vec3 finalColor, vec4 blendSpecular, vec3 bumpNormalTs, vec3 worldNormal, vec3 illumination)
{
    if (g_iRenderMode == renderMode_Color)
    {
        return vec4(SrgbLinearToGamma(finalColor), 1.0);
    }

    if (g_iRenderMode == renderMode_Roughness)
    {
        litColor.rgb = pow2(1 - blendSpecular.xxx);
        return litColor;
    }

    if (g_iRenderMode == renderMode_TerrainBlend)
    {
        return vec4(vBlendWeights.xyz, 1.0);
    }

    if (g_iRenderMode == renderMode_Tint)
    {
        return vec4(vVertexColor.rgb, 1.0);
    }

    if (g_iRenderMode == renderMode_Normals)
    {
        return vec4(PackToColor(vNormalOut), 1.0);
    }

    if (g_iRenderMode == renderMode_BumpNormals)
    {
        return vec4(PackToColor(worldNormal), 1.0);
    }

    if (g_iRenderMode == renderMode_Illumination)
    {
        return vec4(illumination, 1.0);
    }

#if (F_NORMAL_MAP == 1)
    if (g_iRenderMode == renderMode_Tangents)
    {
        return vec4(PackToColor(normalize(vTangentOut.xyz)), 1.0);
    }

    if (g_iRenderMode == renderMode_BumpMap)
    {
        return vec4(PackToColor(bumpNormalTs), 1.0);
    }
#endif

    return litColor;
}

void main()
{
    LayerUvs_t uvs = GetLayerUvs();
    Layers_t colors = SampleLayers(g_tColor0, g_tColor1, g_tColor2, g_tColor3, uvs);
    vec4 weights = GetBlendWeights(colors);
    vec3 finalColor = BlendColors(weights, colors, uvs);

    vec3 bumpNormalTs = GetBumpNormalTs(weights, uvs);
    vec3 worldNormal = CalculateWorldNormal(bumpNormalTs);

    float sunLit;
    vec3 illumination = GetIllumination(worldNormal, sunLit);

#if (F_SPECULAR == 1)
    vec4 blendSpecular = BlendLayers(weights, SampleLayers(g_tSpecular0, g_tSpecular1, g_tSpecular2, g_tSpecular3, uvs));
#else
    vec4 blendSpecular = vec4(0.0);
#endif
    float reflectance = blendSpecular.x * g_flSpecularIntensity;
#if (F_GLOBAL_TINT == 1)
    reflectance *= g_vGlobalTint.a;
#endif
    float specular = CalculateDotaSpecular(worldNormal, vFragPosition) * reflectance;
    vec3 specularAlbedo = finalColor + specular * g_vSpecularAlbedoColor.rgb;

    vec4 litColor = vec4(illumination * specularAlbedo + specular * g_vSpecularLitColor.rgb * sunLit, vVertexColor.a);

    // Self illumination shows the unlit albedo
    litColor.rgb = mix(litColor.rgb, specularAlbedo, blendSpecular.y);

    outputColor = ApplyRenderMode(litColor, finalColor, blendSpecular, bumpNormalTs, worldNormal, illumination);
    ApplyFog(outputColor.rgb, vFragPosition);
}
