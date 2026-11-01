#version 460

// Render modes -- Switched on/off by code
#define renderMode_FullBright 0
#define renderMode_Color 0
#define renderMode_Roughness 0
#define renderMode_Normals 0
#define renderMode_Tangents 0
#define renderMode_BumpMap 0
#define renderMode_BumpNormals 0
#define renderMode_Illumination 0
#define renderMode_Tint 0

//Parameter defines - These are default values and can be overwritten based on material/model parameters
#define F_FULLBRIGHT 0
#define F_SOLID_COLOR 0
#define F_NORMAL_MAP 0
#define F_SPECULAR 0
uniform int F_MODULATE_SPECULAR_BY_ALPHA;
#define F_TINT_MASK 0
#define F_TINT_MASK2 0
#define F_PAINT_VERTEX_COLORS 0
#define F_ALPHA_TEST 0
#define F_TRANSLUCENT 0
#define F_ALLOW_LIGHTING_ON_TRANSLUCENT 0
#define F_SCROLL_UV 0
//End of parameter defines

in vec3 vFragPosition;

in vec3 vNormalOut;
in vec4 vTangentOut;
in vec2 vTexCoordOut;
#if (F_PAINT_VERTEX_COLORS == 1)
    in vec4 vVertexColorOut;
#endif
flat in vec4 vTintColorFadeOut;

out vec4 outputColor;

#if (F_SOLID_COLOR == 0)
    uniform sampler2D g_tColor; // SrgbRead(true) RepresentativeTexture
#endif

#if (F_TINT_MASK == 1 || F_TINT_MASK == 2)
    uniform sampler2D g_tTintMask;
#endif

#if (F_SPECULAR == 1)
    uniform sampler2D g_tSpecular; // SrgbRead(true) Reflectance, SelfIllum, Bloom
    uniform float g_flSpecularIntensity = 1.0;
#endif

#if (F_NORMAL_MAP == 1)
    uniform sampler2D g_tNormal;
    uniform float g_flBumpStrength = 1.0;
#endif

#if (F_SCROLL_UV == 1 || F_SCROLL_UV == 2)
    uniform sampler2D g_tScrollSpeed;
    uniform float g_flScrollUvSpeed;
    uniform vec2 g_vScrollUvDirection;
#endif

#include "../common/utils.slang"
#include "../common/view_constants.slang"

#include "../common/lighting_constants.slang"
#include "../common/lighting_common.slang"
#include "../common/shadowmapping.slang"
#include "../common/fog.slang"
#include "global_light.slang"

uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)
uniform float g_flOpacityScale = 1.0;

uniform float g_flAlphaTestReference = 0.5;

struct TexCoords_t
{
    vec2 Color;
    vec2 All;
    vec2 ScrollTexel;
};

TexCoords_t GetTexCoords()
{
    TexCoords_t coords;
    coords.Color = vTexCoordOut;
    coords.All = vTexCoordOut;
    coords.ScrollTexel = vec2(1.0);

#if (F_SCROLL_UV == 1 || F_SCROLL_UV == 2)
    coords.ScrollTexel = texture(g_tScrollSpeed, coords.All).ga;
    coords.Color = coords.Color + g_vScrollUvDirection.xy * g_flTime * g_flScrollUvSpeed * coords.ScrollTexel.x;
#endif

#if (F_SCROLL_UV == 2)
    coords.All = coords.Color;
#endif

    return coords;
}

vec4 SampleColor(TexCoords_t coords)
{
#if (F_SOLID_COLOR == 0)
    vec4 color = texture(g_tColor, coords.Color);

    #if (F_TRANSLUCENT == 1 && (F_SCROLL_UV == 1 || F_SCROLL_UV == 2))
        color.a = coords.ScrollTexel.y;
    #endif

    return color;
#else
    return vec4(1.0);
#endif
}

vec3 SampleNormalTs(vec2 coords)
{
#if (F_NORMAL_MAP == 1)
    return DecodeDxt5Normal(texture(g_tNormal, coords));
#else
    return vec3(0.0, 0.0, 1.0);
#endif
}

//Calculate the normal of this fragment in world space
vec3 CalculateWorldNormal(vec3 vNormalTs)
{
#if (F_NORMAL_MAP == 1)
    vec3 normal = vNormalOut;
    vec3 tangent = vTangentOut.xyz;
    vec3 bitangent = GetBitangent(vNormalOut, vTangentOut);

    mat3 tangentSpace = mat3(tangent, bitangent, normal);

    return normalize(tangentSpace * vNormalTs);
#else
    return vNormalOut;
#endif
}

vec3 GetIllumination(vec3 worldNormal, out float sunShadow)
{
    sunShadow = 0.0;

    if (g_iRenderMode == renderMode_FullBright)
    {
        return vec3(1.0);
    }

#if F_FULLBRIGHT == 1 || (F_TRANSLUCENT == 1 && F_ALLOW_LIGHTING_ON_TRANSLUCENT == 0)
    return vec3(1.0);
#else
    return CalculateDotaGlobalLight(worldNormal, vFragPosition, sunShadow);
#endif
}

vec3 GetTintFactor(vec2 coords)
{
#if (F_TINT_MASK == 1 || F_TINT_MASK == 2)
    float tintStrength = texture(g_tTintMask, coords).y;
    vec3 tintFactor = mix(vec3(1.0), vTintColorFadeOut.rgb, tintStrength);
#else
    vec3 tintFactor = vTintColorFadeOut.rgb;
#endif

    return tintFactor * g_vColorTint.rgb;
}

vec3 ApplyPaintVertexColors(vec3 color)
{
#if (F_PAINT_VERTEX_COLORS == 1)
    return color * vVertexColorOut.rgb;
#else
    return color;
#endif
}

vec3 SampleSpecularTexel(vec2 coords)
{
#if (F_SPECULAR == 1)
    return texture(g_tSpecular, coords).xyz;
#else
    return vec3(0.0);
#endif
}

vec3 ShadeAlbedo(vec3 albedo, vec3 illumination, float sunShadow, vec3 specularTexel, float alpha, vec3 worldNormal)
{
#if (F_SPECULAR == 1)
    float reflectance = specularTexel.x * g_flSpecularIntensity;
    if (F_MODULATE_SPECULAR_BY_ALPHA == 1)
    {
        reflectance *= alpha * vTintColorFadeOut.a;
    }

    float specular = CalculateDotaSpecular(worldNormal, vFragPosition) * reflectance;
    vec3 specularAlbedo = albedo + specular * g_vSpecularAlbedoColor.rgb;
    vec3 litColor = illumination * specularAlbedo + specular * g_vSpecularLitColor.rgb * sunShadow;

    // Self illumination shows the unlit albedo
    return mix(litColor, specularAlbedo, specularTexel.y);
#else
    return illumination * albedo;
#endif
}

vec4 ApplyRenderMode(vec4 litColor, vec4 color, vec3 specularTexel, vec3 normalTs, vec3 worldNormal, vec3 illumination)
{
    if (g_iRenderMode == renderMode_Color)
    {
        return vec4(color.rgb, 1.0);
    }

#if (F_SPECULAR == 1)
    if (g_iRenderMode == renderMode_Roughness)
    {
        litColor.rgb = pow2(1 - specularTexel.xxx);
        return litColor;
    }
#endif

    if (g_iRenderMode == renderMode_Tangents)
    {
        return vec4(PackToColor(vTangentOut.xyz), 1.0);
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
        return vec4(SrgbLinearToGamma(illumination), 1.0);
    }

#if F_NORMAL_MAP == 1
    if (g_iRenderMode == renderMode_BumpMap)
    {
        litColor.rgb = PackToColor(normalTs);
        return litColor;
    }
#endif

#if F_PAINT_VERTEX_COLORS == 1
    if (g_iRenderMode == renderMode_Tint)
    {
        return vVertexColorOut;
    }
#endif

    return litColor;
}

void main()
{
    TexCoords_t coords = GetTexCoords();

    vec4 color = SampleColor(coords);
    vec3 normalTs = SampleNormalTs(coords.All);
    vec3 worldNormal = CalculateWorldNormal(normalTs);

    ApplyAlphaTest(color.a, g_flAlphaTestReference);

    float sunShadow;
    vec3 illumination = GetIllumination(worldNormal, sunShadow);

    vec3 tintFactor = GetTintFactor(coords.All);
    vec3 specularTexel = SampleSpecularTexel(coords.All);

    vec3 albedo = ApplyPaintVertexColors(color.rgb * tintFactor);
    vec3 litColor = ShadeAlbedo(albedo, illumination, sunShadow, specularTexel, color.a, worldNormal);

    outputColor = ApplyRenderMode(vec4(litColor, color.a), color, specularTexel, normalTs, worldNormal, illumination);
    ApplyFog(outputColor.rgb, vFragPosition);
}
