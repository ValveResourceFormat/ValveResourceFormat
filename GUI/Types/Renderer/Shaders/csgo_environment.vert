#version 460

#if !(defined(csgo_environment_vfx) || defined(csgo_environment_blend_vfx))
    #error "This shader is not supported!"
#endif

#include "common/utils.glsl"

#if defined(csgo_environment_vfx)
#include "common/animation.glsl"
#endif

#include "common/features.glsl"
#include "csgo_environment_features.glsl"

layout (location = 0) in vec3 vPOSITION;
layout (location = 3) in vec2 vTEXCOORD;
#include "common/compression.glsl"

in vec4 vCOLOR;
out vec4 vBlendColorTint;

#if (F_SECONDARY_UV == 1)
    in vec2 vTEXCOORD2;
    out vec2 vTexCoord2;
#else
    vec2 vTexCoord2; // fake reference
#endif

#if D_BAKED_LIGHTING_FROM_LIGHTMAP == 1
    in vec2 vLightmapUV;
    out vec3 vLightmapUVScaled;
#elif D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1
    in vec4 vPerVertexLighting;  // COLOR1
    out vec3 vPerVertexLightingOut;
#endif

#if defined(csgo_environment_blend_vfx)
    in vec4 vTEXCOORD4;
    out vec4 vColorBlendValues;
#endif


out vec3 vFragPosition;
out vec3 vNormalOut;
out vec3 vTangentOut;
out vec3 vBitangentOut;
centroid out vec3 vCentroidNormalOut;
out vec4 vTexCoord;
out vec4 vVertexColor;

uniform vec4 g_vColorTint = vec4(1.0);
uniform float g_flModelTintAmount = 1.0;
uniform float g_flFadeExponent = 1.0;

#include "common/ViewConstants.glsl"
#include "common/LightingConstants.glsl"
uniform mat4 transform;
uniform vec4 vTint;

// Material 1
uniform float g_flTexCoordRotation1 = 0.0;
uniform vec4 g_vTexCoordCenter1 = vec4(0.5);
uniform vec4 g_vTexCoordOffset1 = vec4(0.0);
uniform vec4 g_vTexCoordScale1 = vec4(1.0);

// Material 2
#if defined(csgo_environment_blend_vfx)
    uniform float g_flTexCoordRotation2 = 0.0;
    uniform vec4 g_vTexCoordCenter2 = vec4(0.5);
    uniform vec4 g_vTexCoordOffset2 = vec4(0.0);
    uniform vec4 g_vTexCoordScale2 = vec4(1.0);

    uniform float g_flBlendSoftness2 = 0.0;
#endif

#if (F_DETAIL_NORMAL == 1)
    uniform float g_flDetailTexCoordRotation1 = 0.0;
    uniform vec4 g_vDetailTexCoordCenter1 = vec4(0.5);
    uniform vec4 g_vDetailTexCoordOffset1 = vec4(0.0);
    uniform vec4 g_vDetailTexCoordScale1 = vec4(1.0);

    out vec2 vDetailTexCoords;
#endif

vec4 GetTintColor()
{
    vec4 TintFade = vec4(1.0);
    TintFade.rgb = mix(vec3(1.0), SrgbLinearToGamma(vTint.rgb * g_vColorTint.rgb), g_flModelTintAmount);
    TintFade.a = pow(vTint.a * g_vColorTint.a, g_flFadeExponent);
    return TintFade;
}

void main()
{
    mat4 skinTransform = transform;

    #if defined(csgo_environment_vfx)
        skinTransform *= getSkinMatrix();
    #endif

    vec4 fragPosition = skinTransform * vec4(vPOSITION, 1.0);
    gl_Position = g_matViewToProjection * fragPosition;
    vFragPosition = fragPosition.xyz / fragPosition.w;

    vec3 normal;
    vec4 tangent;
    GetOptionallyCompressedNormalTangent(normal, tangent);

    mat3 normalTransform = transpose(inverse(mat3(skinTransform)));
    vNormalOut = normalize(normalTransform * normal);
    vTangentOut = normalize(normalTransform * tangent.xyz);
    vBitangentOut = tangent.w * cross(vNormalOut, vTangentOut);

	vTexCoord.xy = RotateVector2D(vTEXCOORD.xy,
        g_flTexCoordRotation1,
        g_vTexCoordScale1.xy,
        g_vTexCoordOffset1.xy,
        g_vTexCoordCenter1.xy
    );

#if D_BAKED_LIGHTING_FROM_LIGHTMAP == 1
    vLightmapUVScaled = vec3(vLightmapUV * g_vLightmapUvScale.xy, 0);
#elif D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1
    vec3 Light = vPerVertexLighting.rgb * 6.0 * vPerVertexLighting.a;
    vPerVertexLightingOut = pow2(Light);
#endif

    vVertexColor = GetTintColor();

    // TODO: ApplyVBIBDefaults
    if (vCOLOR.rgba == vec4(0, 0, 0, 1))
    {
        vBlendColorTint = vec4(1.0f);
    }
    else
    {
        vBlendColorTint = vCOLOR / 255.0;
    }

#if (F_SECONDARY_UV == 1)
    vTexCoord2 = vTEXCOORD2.xy;
#endif

#if (F_DETAIL_NORMAL == 1)
    const bool DetailUseSecondaryUV = (F_SECONDARY_UV == 1 && F_DETAIL_NORMAL_USES_SECONDARY_UVS == 1);
    vDetailTexCoords = RotateVector2D(DetailUseSecondaryUV ? vTexCoord2 : vTexCoord.xy,
        g_flDetailTexCoordRotation1,
        g_vDetailTexCoordScale1.xy,
        g_vDetailTexCoordOffset1.xy,
        g_vDetailTexCoordCenter1.xy
    );
#endif

#if defined(csgo_environment_blend_vfx)
    vTexCoord.zw = RotateVector2D(vTEXCOORD.xy,
        g_flTexCoordRotation2,
        g_vTexCoordScale2.xy,
        g_vTexCoordOffset2.xy,
        g_vTexCoordCenter2.xy
    );

    vColorBlendValues = vTEXCOORD4 / 255.0f;
    vColorBlendValues.a = clamp(vColorBlendValues.a + g_flBlendSoftness2, 0.001, 1.0);
#endif

    vCentroidNormalOut = vNormalOut;
}
