#version 460

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

uniform vec3 g_vColorTint = vec3(1.0);
uniform float g_flModelTintAmount = 1.0;

#include "common/ViewConstants.glsl"
#include "common/LightingConstants.glsl"
uniform mat4 transform;
uniform vec4 vTint;

// Material 1
uniform float g_flTexCoordRotation1 = 0.0;
uniform vec2 g_vTexCoordCenter1 = vec2(0.5);
uniform vec2 g_vTexCoordOffset1 = vec2(0.0);
uniform vec2 g_vTexCoordScale1 = vec2(1.0);

// Material 2
#if defined(csgo_environment_blend_vfx)
    uniform float g_flTexCoordRotation2 = 0.0;
    uniform vec2 g_vTexCoordCenter2 = vec2(0.5);
    uniform vec2 g_vTexCoordOffset2 = vec2(0.0);
    uniform vec2 g_vTexCoordScale2 = vec2(1.0);

    uniform float g_flBlendSoftness2 = 0.01;

    uniform int F_BLEND_BY_FACING_DIRECTION_2; // 0="None", 1="Geometric", 2="Normal Map"

    uniform vec3 g_vFacingDirection2 = vec3(0.0, 0.0, 1.0);
    #define g_vFacingDirectionNormalizedSafe2 (normalize(vec3(g_vFacingDirection2.x,g_vFacingDirection2.y,g_vFacingDirection2.z+((g_vFacingDirection2.z==0) ? .0001 : 0))))

    uniform float g_flFacingDirectionMaskSpread2 = 0.5;
    uniform float g_vFacingDirectionMaskFalloff2 = 0.1;
    #define g_vFacingDirectionMinMax2 (vec2(max(0, (1-g_flFacingDirectionMaskSpread2)-g_vFacingDirectionMaskFalloff2), min(1,((1-g_flFacingDirectionMaskSpread2)+.001)+g_vFacingDirectionMaskFalloff2)))


    #if (F_ENABLE_LAYER_3 == 1)
        uniform float g_flTexCoordRotation3 = 0.0;
        uniform vec2 g_vTexCoordCenter3 = vec2(0.5);
        uniform vec2 g_vTexCoordOffset3 = vec2(0.0);
        uniform vec2 g_vTexCoordScale3 = vec2(1.0);

        uniform float g_flBlendSoftness3 = 0.01;

        uniform int F_BLEND_BY_FACING_DIRECTION_3; // 0="None", 1="Geometric", 2="Normal Map"

        uniform vec3 g_vFacingDirection3 = vec3(0.0, 0.0, 1.0);
        #define g_vFacingDirectionNormalizedSafe3 (normalize(vec3(g_vFacingDirection3.x,g_vFacingDirection3.y,g_vFacingDirection3.z+((g_vFacingDirection3.z==0) ? .0001 : 0))))

        uniform float g_flFacingDirectionMaskSpread3 = 0.5;
        uniform float g_vFacingDirectionMaskFalloff3 = 0.1;
        #define g_vFacingDirectionMinMax3 (vec2(max(0, (1-g_flFacingDirectionMaskSpread3)-g_vFacingDirectionMaskFalloff3), min(1,((1-g_flFacingDirectionMaskSpread3)+.001)+g_vFacingDirectionMaskFalloff3)))

        out vec4 vTexCoord3;
    #endif



    #if (F_SHARED_COLOR_OVERLAY == 1)
        uniform float g_flOverlayTexCoordRotation = 0.0;
        uniform vec2 g_vOverlayTexCoordCenter = vec2(0.5);
        uniform vec2 g_vOverlayTexCoordOffset = vec2(0.0);
        uniform vec2 g_vOverlayTexCoordScale = vec2(1.0);
    #endif

#endif

#if (F_DETAIL_NORMAL == 1)
    uniform float g_flDetailTexCoordRotation1 = 0.0;
    uniform vec2 g_vDetailTexCoordCenter1 = vec2(0.5);
    uniform vec2 g_vDetailTexCoordOffset1 = vec2(0.0);
    uniform vec2 g_vDetailTexCoordScale1 = vec2(1.0);

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
    vVertexPaint =  mix(vec3(1.0), vCOLOR.rgb, vec3(vCOLOR.a));

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

        #if (F_ENABLE_LAYER_3 == 1)
            vTexCoord3.xy = RotateVector2D(vTEXCOORD.xy,
                g_flTexCoordRotation3,
                g_vTexCoordScale3.xy,
                g_vTexCoordOffset3.xy,
                g_vTexCoordCenter3.xy
            );

            vTexCoord3.zw = vTEXCOORD.xy;
        #endif

        vColorBlendValues = vTEXCOORD4;

        if (F_BLEND_BY_FACING_DIRECTION_2 > 0)
        {
            float flDirectionMultiplier = fma(dot(g_vFacingDirectionNormalizedSafe2.xyz, vNormalOut.xyz), 0.5, 0.5);
            flDirectionMultiplier = smoothstep(g_vFacingDirectionMinMax2.x, g_vFacingDirectionMinMax2.y, flDirectionMultiplier);

            vColorBlendValues.x *= flDirectionMultiplier;
        }

        float flSoftness = g_flBlendSoftness2;

        #if (F_ENABLE_LAYER_3 == 1)
            if (vColorBlendValues.x < 0.001)
            {
                flSoftness = g_flBlendSoftness3;
            }

            flSoftness = mix(flSoftness, g_flBlendSoftness3, vColorBlendValues.y);

            if (F_BLEND_BY_FACING_DIRECTION_3 > 0)
            {
                float flDirectionMultiplier = fma(dot(g_vFacingDirectionNormalizedSafe3.xyz, vNormalOut.xyz), 0.5, 0.5);
                flDirectionMultiplier = smoothstep(g_vFacingDirectionMinMax3.x, g_vFacingDirectionMinMax3.y, flDirectionMultiplier);

                vColorBlendValues.y *= flDirectionMultiplier;
                // bVertexBlendByFacingDirection3 ???
            }
        #endif

        vColorBlendValues.a = clamp(vColorBlendValues.a + flSoftness, 0.001, 1.0);

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
