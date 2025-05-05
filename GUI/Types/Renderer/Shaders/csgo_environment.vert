#version 460

#if !(defined(csgo_environment_vfx) || defined(csgo_environment_blend_vfx))
    #error "This shader is not supported!"
#endif

#include "common/utils.glsl"
#include "common/animation.glsl"
#include "common/features.glsl"
#include "csgo_environment_features.glsl"

layout (location = 0) in vec3 vPOSITION;
layout (location = 3) in vec2 vTEXCOORD;
#include "common/compression.glsl"

in vec4 vCOLOR;

#if (F_SECONDARY_UV == 1)
    in vec2 vTEXCOORD1;
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
out vec4 vTexCoord2;
out vec4 vTintColor_ModelAmount;
centroid out vec4 vVertexColor_Alpha;

uniform vec4 g_vColorTint = vec4(1.0);
uniform float g_flModelTintAmount = 1.0;

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

    uniform float g_flBlendSoftness2 = 0.01;

    #if (F_SHARED_COLOR_OVERLAY == 1)
        uniform float g_flOverlayTexCoordRotation = 0.0;
        uniform vec4 g_vOverlayTexCoordCenter = vec4(0.5);
        uniform vec4 g_vOverlayTexCoordOffset = vec4(0.0);
        uniform vec4 g_vOverlayTexCoordScale = vec4(1.0);
    #endif

#endif

#if (F_DETAIL_NORMAL == 1)
    uniform float g_flDetailTexCoordRotation1 = 0.0;
    uniform vec4 g_vDetailTexCoordCenter1 = vec4(0.5);
    uniform vec4 g_vDetailTexCoordOffset1 = vec4(0.0);
    uniform vec4 g_vDetailTexCoordScale1 = vec4(1.0);

    out vec2 vDetailTexCoords;
#endif


void main()
{
    mat4 skinTransform = transform * getSkinMatrix();
    vec4 fragPosition = skinTransform * vec4(vPOSITION, 1.0);
    gl_Position = g_matViewToProjection * fragPosition;
    vFragPosition = fragPosition.xyz / fragPosition.w;

    vec3 normal;
    vec4 tangent;
    GetOptionallyCompressedNormalTangent(normal, tangent);

    mat3 normalTransform = adjoint(skinTransform);
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

    // original code has SrgbGammaToLinear
    vTintColor_ModelAmount.rgb = (vTint.rgb);
    float flLowestPoint = min(vTintColor_ModelAmount.r, min(vTintColor_ModelAmount.g, vTintColor_ModelAmount.b));
    vTintColor_ModelAmount.a = g_flModelTintAmount * (1.0 - flLowestPoint);

    vec3 vVertexPaint = vec3(1.0);

    // TODO: ApplyVBIBDefaults
    if (vCOLOR.rgba != vec4(0, 0, 0, 1))
    {
        vVertexPaint =  mix(vec3(1.0), vCOLOR.rgb, vec3(vCOLOR.a));
    }

    vVertexColor_Alpha = vec4(SrgbGammaToLinear(g_vColorTint.rgb) * vVertexPaint, vTint.a);

    #if (F_SECONDARY_UV == 1)
        vTexCoord2.zw = vTEXCOORD1.xy;
    #endif

    #if (F_DETAIL_NORMAL == 1)
        const bool bDetailNormalUsesUV2 = (F_SECONDARY_UV == 1 && F_DETAIL_NORMAL_USES_SECONDARY_UVS == 1);
        vDetailTexCoords = RotateVector2D(bDetailNormalUsesUV2 ? vTexCoord2.zw : vTEXCOORD.xy,
            g_flDetailTexCoordRotation1,
            g_vDetailTexCoordScale1.xy,
            g_vDetailTexCoordOffset1.xy,
            g_vDetailTexCoordCenter1.xy
        );
    #endif

    #if defined(csgo_environment_blend_vfx)
        vTexCoord2.xy = RotateVector2D(vTEXCOORD.xy,
            g_flTexCoordRotation2,
            g_vTexCoordScale2.xy,
            g_vTexCoordOffset2.xy,
            g_vTexCoordCenter2.xy
        );

        vColorBlendValues = vTEXCOORD4;
        vColorBlendValues.a = clamp(vColorBlendValues.a + g_flBlendSoftness2, 0.001, 1.0);

        #if (F_SHARED_COLOR_OVERLAY == 1)
            vTexCoord.zw = RotateVector2D((F_SECONDARY_UV == 1) ? vTexCoord2.zw : vTEXCOORD.xy,
                g_flOverlayTexCoordRotation,
                g_vOverlayTexCoordScale.xy,
                g_vOverlayTexCoordOffset.xy,
                g_vOverlayTexCoordCenter.xy
            );
        #endif

    #endif

    vCentroidNormalOut = vNormalOut;
}
