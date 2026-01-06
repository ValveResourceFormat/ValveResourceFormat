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
#define F_TINT_MASK2 0 // what does this do?
#define F_PAINT_VERTEX_COLORS 0
#define F_ALPHA_TEST 0
#define F_TRANSLUCENT 0
#define F_ALLOW_LIGHTING_ON_TRANSLUCENT 0
#define F_SCROLL_UV 0
//End of parameter defines

in vec3 vFragPosition;

in vec3 vNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;
in vec2 vTexCoordOut;
#if (F_PAINT_VERTEX_COLORS == 1)
    in vec4 vVertexColorOut;
#endif
flat in vec4 vTintColorFadeOut;

out vec4 outputColor;

#if (F_SOLID_COLOR == 0)
    uniform sampler2D g_tColor; // SrgbRead(true)
#endif

#if (F_TINT_MASK == 1 || F_TINT_MASK == 2)
    uniform sampler2D g_tTintMask;
#endif

#if (F_SPECULAR == 1)
    uniform sampler2D g_tSpecular; // Reflectance, SelfIllum, Bloom
    uniform vec3 g_vColorTint2 = vec3(1.0); // SrgbRead(true)
    uniform float g_flSpecularIntensity;
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

#include "common/utils.slang"
#include "common/ViewConstants.slang"

#include "common/LightingConstants.slang"
#include "common/lighting_common.slang"
#include "common/shadowmapping.slang"

uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)
uniform float g_flOpacityScale = 1.0;

uniform float g_flAlphaTestReference = 0.5;

//Calculate the normal of this fragment in world space
vec3 calculateWorldNormal(vec3 vNormalTs)
{
    vec3 normal = vNormalOut;
    vec3 tangent = vTangentOut.xyz;
    vec3 bitangent = vBitangentOut;

    //Make the tangent space matrix
    mat3 tangentSpace = mat3(tangent, bitangent, normal);

    //Calculate the tangent normal in world space and return it
    return normalize(tangentSpace * vNormalTs);
}


void main()
{
    vec2 coordsColor = vTexCoordOut;
    vec2 coordsAll = vTexCoordOut;

    #if (F_SCROLL_UV == 1 || F_SCROLL_UV == 2)
        vec2 scrollTexel = texture(g_tScrollSpeed, coordsAll).ga;
        coordsColor = coordsColor + g_vScrollUvDirection.xy * g_flTime * g_flScrollUvSpeed * scrollTexel.x;
        #if (F_SCROLL_UV == 2)
            coordsAll = coordsColor;
        #endif
    #endif

    #if (F_SOLID_COLOR == 0)
        vec4 color = texture(g_tColor, coordsColor);
        #if (F_TRANSLUCENT == 1 && (F_SCROLL_UV == 1 || F_SCROLL_UV == 2))
            color.a = scrollTexel.y;
        #endif
    #else
        vec4 color = vec4(1.0);
    #endif

    #if (F_NORMAL_MAP == 1)
        vec4 vNormalTexel = texture(g_tNormal, coordsAll);
        vec3 vNormalTs = DecodeDxt5Normal(vNormalTexel);
        vec3 worldNormal = calculateWorldNormal(vNormalTs);
    #else
        vec3 worldNormal = vNormalOut;
    #endif

#if F_ALPHA_TEST == 1
    if (color.a < g_flAlphaTestReference)
    {
       discard;
    }
#endif

    vec3 lightDirection = GetEnvLightDirection(0u);
    vec3 viewDirection = normalize(g_vCameraPositionWs - vFragPosition);

#if F_FULLBRIGHT == 1 || (F_TRANSLUCENT == 1 && F_ALLOW_LIGHTING_ON_TRANSLUCENT == 0)
    float illumination = 1.0;
#else
    //Calculate lambert lighting
    float illumination = max(0.0, dot(worldNormal, lightDirection));
    illumination *= CalculateSunShadowMapVisibility(vFragPosition);
    illumination = illumination + 0.6;
    illumination = pow2(illumination);
#endif

    if (g_iRenderMode == renderMode_FullBright)
    {
        illumination = 1.0;
    }

    //Apply tint
#if (F_TINT_MASK == 1 || F_TINT_MASK == 2)
    float tintStrength = texture(g_tTintMask, coordsAll).y;
    vec3 tintFactor = mix(vec3(1.0), vTintColorFadeOut.rgb, tintStrength);
#else
    vec3 tintFactor = vTintColorFadeOut.rgb;
#endif

    tintFactor *= g_vColorTint.rgb;

    //Simply multiply the color from the color texture with the illumination
    outputColor = vec4(illumination * 2.0 * color.rgb * tintFactor, color.a);

    #if (F_PAINT_VERTEX_COLORS == 1)
        outputColor.rgb *= vVertexColorOut.rgb;
    #endif

    #if (F_SPECULAR == 1)
        vec3 specularTexel = texture(g_tSpecular, coordsAll).xyz;
        float NoL = max(dot(lightDirection, worldNormal), 0.0);
        float specular = pow(NoL, 6.0);
        if (F_MODULATE_SPECULAR_BY_ALPHA == 1)
        {
            specular *= color.a * vTintColorFadeOut.a;
        }
        outputColor.rgb *= vec3(1.0) + specularTexel.x * specular * g_flSpecularIntensity * SrgbGammaToLinear(g_vColorTint2.xyz);
        outputColor.rgb += specularTexel.y;
    #endif

    if (g_iRenderMode == renderMode_Color)
    {
        outputColor = vec4(color.rgb, 1.0);
    }
#if (F_SPECULAR == 1)
    else if (g_iRenderMode == renderMode_Roughness)
    {
        outputColor.rgb = pow2(1 - specularTexel.xxx);
    }
#endif
    else if (g_iRenderMode == renderMode_Tangents)
    {
        outputColor = vec4(PackToColor(vTangentOut.xyz), 1.0);
    }
    else if (g_iRenderMode == renderMode_Normals)
    {
        outputColor = vec4(PackToColor(vNormalOut), 1.0);
    }
    else if (g_iRenderMode == renderMode_BumpNormals)
    {
        outputColor = vec4(PackToColor(worldNormal), 1.0);
    }
    else if (g_iRenderMode == renderMode_Illumination)
    {
        outputColor = vec4(SrgbLinearToGamma(vec3(illumination)), 1.0);
    }
#if F_NORMAL_MAP == 1
    else if (g_iRenderMode == renderMode_BumpMap)
    {
        outputColor.rgb = PackToColor(vNormalTs);
    }
#endif
#if F_PAINT_VERTEX_COLORS == 1
    else if (g_iRenderMode == renderMode_Tint)
    {
        outputColor = vVertexColorOut;
    }
#endif
}
