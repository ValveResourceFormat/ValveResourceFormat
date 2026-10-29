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
#define F_NORMAL_MAP 0
#define F_TWO_LAYER_BLEND 0
#define F_WORLDSPACE_UVS 0
//End of parameter defines

in vec3 vFragPosition;

in vec3 vNormalOut;
in vec4 vTangentOut;

in vec4 vBlendWeights;
in vec4 vBlendAlphas;
in vec4 vVertexColor;

in vec2 vTexCoordOut;
in vec2 vTexCoord1Out;
#if (F_TWO_LAYER_BLEND == 0)
    in vec2 vTexCoord2Out;
    in vec2 vTexCoord3Out;
#endif

out vec4 outputColor;

uniform sampler2D g_tColor0; // SrgbRead(true)
uniform sampler2D g_tColor1; // SrgbRead(true)
uniform sampler2D g_tColor2; // SrgbRead(true)
uniform sampler2D g_tColor3; // SrgbRead(true)

uniform sampler2D g_tNormal0;
uniform sampler2D g_tNormal1;
uniform sampler2D g_tNormal2;
uniform sampler2D g_tNormal3;

uniform sampler2D g_tSpecular0; // SrgbRead(true)
uniform sampler2D g_tSpecular1; // SrgbRead(true)
uniform sampler2D g_tSpecular2; // SrgbRead(true)
uniform sampler2D g_tSpecular3; // SrgbRead(true)

#if (F_TINT_MASK == 1)
    uniform sampler2D g_tTintMasks;
#endif

uniform vec3 g_vGlobalTint = vec3(1.0); // SrgbRead(true)
uniform vec3 g_vColorTint0 = vec3(1.0); // SrgbRead(true)
uniform vec3 g_vColorTint1 = vec3(1.0); // SrgbRead(true)
uniform vec3 g_vColorTint2 = vec3(1.0); // SrgbRead(true)
uniform vec3 g_vColorTint3 = vec3(1.0); // SrgbRead(true)
uniform vec3 g_vColorTintB0 = vec3(1.0); // SrgbRead(true)
uniform vec3 g_vColorTintB1 = vec3(1.0); // SrgbRead(true)
uniform vec3 g_vColorTintB2 = vec3(1.0); // SrgbRead(true)
uniform vec3 g_vColorTintB3 = vec3(1.0); // SrgbRead(true)

#include "../common/utils.slang"
#include "../common/view_constants.slang"

uniform float g_flBumpStrength = 1.0;
uniform float g_flSpecularIntensity = 1.0;

#include "../common/lighting_constants.slang"
#include "../common/lighting_common.slang"
#include "../common/shadowmapping.slang"
#include "../common/fog.slang"
#include "global_light.slang"

// One value per layer. The third and fourth are zero when only two layers are blended.
struct Layers_t
{
    vec4 Layer0;
    vec4 Layer1;
    vec4 Layer2;
    vec4 Layer3;
};

// TODO: F_PARALLAX_MAP_ON_0..3 offset each enabled layer's texcoords before any sampling by its texcoord rotation and
// scale applied to (dot(view, tangent), dot(view, bitangent)) * g_tParallaxMask[layer] * g_flParallaxStrength{layer},
// where view points from the camera to the fragment.
Layers_t SampleLayers(sampler2D texture0, sampler2D texture1, sampler2D texture2, sampler2D texture3)
{
    Layers_t layers;
    layers.Layer0 = texture(texture0, vTexCoordOut);
    layers.Layer1 = texture(texture1, vTexCoord1Out);
#if F_TWO_LAYER_BLEND == 0
    layers.Layer2 = texture(texture2, vTexCoord2Out);
    layers.Layer3 = texture(texture3, vTexCoord3Out);
#else
    layers.Layer2 = vec4(0.0);
    layers.Layer3 = vec4(0.0);
#endif
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

#if F_TWO_LAYER_BLEND == 0
    float blendWeight2 = ApplyBlendModulation(vBlendWeights.y, colors.Layer2.w, vBlendAlphas.y);
    blendWeight2 = clamp(blendWeight2, 0.0, invBlendWeight1);

    float invBlendWeight2 = invBlendWeight1 - blendWeight2;

    float blendWeight3 = ApplyBlendModulation(vBlendWeights.z, colors.Layer3.w, vBlendAlphas.z);
    blendWeight3 = clamp(blendWeight3, 0.0, invBlendWeight2);

    // remaining weight
    float blendWeight0 = invBlendWeight2 - blendWeight3;

    return vec4(blendWeight0, blendWeight1, blendWeight2, blendWeight3);
#else
    return vec4(invBlendWeight1, blendWeight1, 0.0, 0.0);
#endif
}

// Interpolate between two tint colors based on the tint mask.
vec3 GetLayerTint(int id, vec3 tintBase, vec3 tint, vec2 coords)
{
#if (F_TINT_MASK == 1)
    float maskValue = texture(g_tTintMasks, coords)[id];
    return mix(tintBase, tint, maskValue);
#else
    return tint;
#endif
}

vec3 BlendColors(vec4 weights, Layers_t colors)
{
    vec3 tint0 = GetLayerTint(0, g_vColorTintB0.rgb, g_vColorTint0.rgb, vTexCoordOut);
    vec3 tint1 = GetLayerTint(1, g_vColorTintB1.rgb, g_vColorTint1.rgb, vTexCoord1Out);

    vec3 c0 = weights.x * colors.Layer0.rgb * tint0;
    vec3 c1 = weights.y * colors.Layer1.rgb * tint1;
    vec3 blended = c0 + c1;

#if (F_TWO_LAYER_BLEND == 0)
    vec3 tint2 = GetLayerTint(2, g_vColorTintB2.rgb, g_vColorTint2.rgb, vTexCoord2Out);
    vec3 tint3 = GetLayerTint(3, g_vColorTintB3.rgb, g_vColorTint3.rgb, vTexCoord3Out);

    vec3 c2 = weights.z * colors.Layer2.rgb * tint2;
    vec3 c3 = weights.w * colors.Layer3.rgb * tint3;
    blended += c2;
    blended += c3;
#endif

    return blended * (vVertexColor.rgb * g_vGlobalTint.rgb);
}

// Tangent space normal of the blended normal maps
vec3 GetBumpNormalTs(vec4 weights)
{
#if (F_NORMAL_MAP == 1)
    // horribly, they actually correct normal orientations to match with texcoord rotation. We're not doing that.
    vec4 bumpNormal = BlendLayers(weights, SampleLayers(g_tNormal0, g_tNormal1, g_tNormal2, g_tNormal3));

    vec3 bumpNormalTs = DecodeDxt5Normal(bumpNormal);
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

vec3 GetIllumination(vec3 worldNormal, out float sunShadow)
{
    sunShadow = 0.0;

    if (g_iRenderMode == renderMode_FullBright)
    {
        return vec3(1.0);
    }

    return CalculateDotaGlobalLight(worldNormal, vFragPosition, sunShadow);
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
    Layers_t colors = SampleLayers(g_tColor0, g_tColor1, g_tColor2, g_tColor3);
    vec4 weights = GetBlendWeights(colors);
    vec3 finalColor = BlendColors(weights, colors);

    vec3 bumpNormalTs = GetBumpNormalTs(weights);
    vec3 worldNormal = CalculateWorldNormal(bumpNormalTs);

    float sunShadow;
    vec3 illumination = GetIllumination(worldNormal, sunShadow);

    vec4 blendSpecular = BlendLayers(weights, SampleLayers(g_tSpecular0, g_tSpecular1, g_tSpecular2, g_tSpecular3));
    float reflectance = blendSpecular.x * g_flSpecularIntensity;
    float specular = CalculateDotaSpecular(worldNormal, vFragPosition) * reflectance;
    vec3 specularAlbedo = finalColor + specular * g_vSpecularAlbedoColor.rgb;

    vec4 litColor = vec4(illumination * specularAlbedo + specular * g_vSpecularLitColor.rgb * sunShadow, vVertexColor.a);

    // Self illumination shows the unlit albedo
    litColor.rgb = mix(litColor.rgb, specularAlbedo, blendSpecular.y);

    outputColor = ApplyRenderMode(litColor, finalColor, blendSpecular, bumpNormalTs, worldNormal, illumination);
    ApplyFog(outputColor.rgb, vFragPosition);
}
