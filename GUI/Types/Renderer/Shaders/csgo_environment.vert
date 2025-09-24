#version 460

#include "common/utils.glsl"
#include "common/instancing.glsl"
#include "common/animation.glsl"
#include "common/features.glsl"
#include "csgo_environment_features.glsl"

layout (location = 0) in vec3 vPOSITION;
layout (location = 3) in vec2 vTEXCOORD;
#include "common/compression.glsl"

in vec4 vCOLOR;
in vec2 vTEXCOORD1;

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

uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)
uniform float g_flModelTintAmount = 1.0;

#include "common/ViewConstants.glsl"
#include "common/LightingConstants.glsl"

// Material 1
uniform int g_nUVSet1 = 1; // 0=Biplanar, 1=UV1, 2=UV2
uniform float g_flTexCoordRotation1 = 0.0;
uniform vec2 g_vTexCoordCenter1 = vec2(0.5);
uniform vec2 g_vTexCoordOffset1 = vec2(0.0);
uniform vec2 g_vTexCoordScale1 = vec2(1.0);

#if (F_DETAIL_NORMAL == 1)
    uniform int g_nDetailUVSet1 = -1; // -1=Inherit, 0=Biplanar, 1=UV1, 2=UV2
    uniform float g_flDetailTexCoordRotation1 = 0.0;
    uniform vec2 g_vDetailTexCoordCenter1 = vec2(0.5);
    uniform vec2 g_vDetailTexCoordOffset1 = vec2(0.0);
    uniform vec2 g_vDetailTexCoordScale1 = vec2(1.0);

    out vec2 vDetailTexCoords;
#endif

// Material 2
#if defined(csgo_environment_blend_vfx)
    uniform int g_nUVSet2 = 1;
    uniform float g_flTexCoordRotation2 = 0.0;
    uniform vec2 g_vTexCoordCenter2 = vec2(0.5);
    uniform vec2 g_vTexCoordOffset2 = vec2(0.0);
    uniform vec2 g_vTexCoordScale2 = vec2(1.0);

    uniform float g_flBlendSoftness2 = 0.01;

    uniform int F_BLEND_BY_FACING_DIRECTION_2; // 0="None", 1="Geometric", 2="Normal Map"
    #define bGeoBlendByFacingDirection2 F_BLEND_BY_FACING_DIRECTION_2 > 0

    uniform vec3 g_vFacingDirection2 = vec3(0.0, 0.0, 1.0);
    #define g_vFacingDirectionNormalizedSafe2 (normalize(vec3(g_vFacingDirection2.x,g_vFacingDirection2.y,g_vFacingDirection2.z+((g_vFacingDirection2.z==0) ? .0001 : 0))))

    uniform float g_flFacingDirectionMaskSpread2 = 0.5;
    uniform float g_vFacingDirectionMaskFalloff2 = 0.1;
    #define g_vFacingDirectionMinMax2 (vec2(max(0, (1-g_flFacingDirectionMaskSpread2)-g_vFacingDirectionMaskFalloff2), min(1,((1-g_flFacingDirectionMaskSpread2)+.001)+g_vFacingDirectionMaskFalloff2)))

    #if (F_DETAIL_NORMAL == 1)
        uniform int g_nDetailUVSet2 = -1;
        uniform float g_flDetailTexCoordRotation2 = 0.0;
        uniform vec2 g_vDetailTexCoordCenter2 = vec2(0.5);
        uniform vec2 g_vDetailTexCoordOffset2 = vec2(0.0);
        uniform vec2 g_vDetailTexCoordScale2 = vec2(1.0);
    #endif

    #if (F_ENABLE_LAYER_3 == 1)
        uniform int g_nUVSet3 = 1;
        uniform float g_flTexCoordRotation3 = 0.0;
        uniform vec2 g_vTexCoordCenter3 = vec2(0.5);
        uniform vec2 g_vTexCoordOffset3 = vec2(0.0);
        uniform vec2 g_vTexCoordScale3 = vec2(1.0);

        uniform float g_flBlendSoftness3 = 0.01;

        uniform int F_BLEND_BY_FACING_DIRECTION_3; // 0="None", 1="Geometric", 2="Normal Map"
        #define bVertexBlendByFacingDirection3 F_BLEND_BY_FACING_DIRECTION_3 == 1
        #define bGeoBlendByFacingDirection3 F_BLEND_BY_FACING_DIRECTION_3 > 0

        uniform vec3 g_vFacingDirection3 = vec3(0.0, 0.0, 1.0);
        #define g_vFacingDirectionNormalizedSafe3 (normalize(vec3(g_vFacingDirection3.x,g_vFacingDirection3.y,g_vFacingDirection3.z+((g_vFacingDirection3.z==0) ? .0001 : 0))))

        uniform float g_flFacingDirectionMaskSpread3 = 0.5;
        uniform float g_vFacingDirectionMaskFalloff3 = 0.1;
        #define g_vFacingDirectionMinMax3 (vec2(max(0, (1-g_flFacingDirectionMaskSpread3)-g_vFacingDirectionMaskFalloff3), min(1,((1-g_flFacingDirectionMaskSpread3)+.001)+g_vFacingDirectionMaskFalloff3)))

        #if (F_DETAIL_NORMAL == 1)
            uniform int g_nDetailUVSet3 = -1;
            uniform float g_flDetailTexCoordRotation3 = 0.0;
            uniform vec2 g_vDetailTexCoordCenter3 = vec2(0.5);
            uniform vec2 g_vDetailTexCoordOffset3 = vec2(0.0);
            uniform vec2 g_vDetailTexCoordScale3 = vec2(1.0);
        #endif

        out vec4 vTexCoord3;
    #endif



    #if (F_SHARED_COLOR_OVERLAY == 1)
        uniform int g_nColorOverlayUVSet = 2;
        uniform float g_flOverlayTexCoordRotation = 0.0;
        uniform vec2 g_vOverlayTexCoordCenter = vec2(0.5);
        uniform vec2 g_vOverlayTexCoordOffset = vec2(0.0);
        uniform vec2 g_vOverlayTexCoordScale = vec2(1.0);
    #endif

#endif


void main()
{
    ObjectData_t object = GetObjectData();

    mat4 skinTransform = object.transform * getSkinMatrix();
    vec4 fragPosition = skinTransform * vec4(vPOSITION, 1.0);
    gl_Position = g_matWorldToProjection * fragPosition;
    vFragPosition = fragPosition.xyz;

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

    vTintColor_ModelAmount.rgb = SrgbGammaToLinear(object.vTint.rgb);
    float flLowestPoint = min(vTintColor_ModelAmount.r, min(vTintColor_ModelAmount.g, vTintColor_ModelAmount.b));
    vTintColor_ModelAmount.a = g_flModelTintAmount * (1.0 - flLowestPoint);

    vec3 vVertexPaint = vec3(1.0);
    vVertexPaint = mix(vec3(1.0), vCOLOR.rgb, vec3(vCOLOR.a));

    vVertexColor_Alpha = vec4(g_vColorTint.rgb * vVertexPaint, object.vTint.a);

    // Set up UV coordinates for all texture layers
    vec4 baseUVs = vec4(vTEXCOORD.xy, vTEXCOORD1.xy);

    vec2 selectedUVs1 = (g_nUVSet1 == 2) ? baseUVs.zw : baseUVs.xy;
    vTexCoord.xy = RotateVector2D(selectedUVs1,
        g_flTexCoordRotation1,
        g_vTexCoordScale1.xy,
        g_vTexCoordOffset1.xy,
        g_vTexCoordCenter1.xy
    );

    #if defined(csgo_environment_blend_vfx)
        vec2 selectedUVs2 = (g_nUVSet2 == 2) ? baseUVs.zw : baseUVs.xy;
        vTexCoord2.xy = RotateVector2D(selectedUVs2,
            g_flTexCoordRotation2,
            g_vTexCoordScale2.xy,
            g_vTexCoordOffset2.xy,
            g_vTexCoordCenter2.xy
        );

        #if (F_ENABLE_LAYER_3 == 1)
            vec2 selectedUVs3 = (g_nUVSet3 == 2) ? baseUVs.zw : baseUVs.xy;
            vTexCoord3.xy = RotateVector2D(selectedUVs3,
                g_flTexCoordRotation3,
                g_vTexCoordScale3.xy,
                g_vTexCoordOffset3.xy,
                g_vTexCoordCenter3.xy
            );
            vTexCoord3.zw = baseUVs.xy;
        #endif

        #if (F_SHARED_COLOR_OVERLAY == 1)
            if (g_nColorOverlayUVSet > 0) {
                vec2 selectedUVsOverlay = (g_nColorOverlayUVSet == 2) ? baseUVs.zw : baseUVs.xy;
                vTexCoord.zw = RotateVector2D(selectedUVsOverlay,
                    g_flOverlayTexCoordRotation,
                    g_vOverlayTexCoordScale.xy,
                    g_vOverlayTexCoordOffset.xy,
                    g_vOverlayTexCoordCenter.xy
                );
            }
        #endif

        vTexCoord2.zw = baseUVs.zw;
    #endif

    #if (F_DETAIL_NORMAL == 1)
        int actualDetailUVSet = (g_nDetailUVSet1 == -1) ? g_nUVSet1 : g_nDetailUVSet1;
        vec2 detailUVs = (actualDetailUVSet == 2) ? vTEXCOORD1.xy : vTEXCOORD.xy;

        vDetailTexCoords = RotateVector2D(detailUVs,
            g_flDetailTexCoordRotation1,
            g_vDetailTexCoordScale1.xy,
            g_vDetailTexCoordOffset1.xy,
            g_vDetailTexCoordCenter1.xy
        );

        #if defined(csgo_environment_blend_vfx)
            int actualDetailUVSet2 = (g_nDetailUVSet2 == -1) ? g_nUVSet2 : g_nDetailUVSet2;
            vec2 detailUVs2 = (actualDetailUVSet2 == 2) ? vTEXCOORD1.xy : vTEXCOORD.xy;
            vTexCoord2.zw = RotateVector2D(detailUVs2,
                g_flDetailTexCoordRotation2,
                g_vDetailTexCoordScale2.xy,
                g_vDetailTexCoordOffset2.xy,
                g_vDetailTexCoordCenter2.xy
            );

            #if (F_ENABLE_LAYER_3 == 1)
                int actualDetailUVSet3 = (g_nDetailUVSet3 == -1) ? g_nUVSet3 : g_nDetailUVSet3;
                vec2 detailUVs3 = (actualDetailUVSet3 == 2) ? vTEXCOORD1.xy : vTEXCOORD.xy;
                vTexCoord3.zw = RotateVector2D(detailUVs3,
                    g_flDetailTexCoordRotation3,
                    g_vDetailTexCoordScale3.xy,
                    g_vDetailTexCoordOffset3.xy,
                    g_vDetailTexCoordCenter3.xy
                );
            #endif
        #endif
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

        if (bGeoBlendByFacingDirection2)
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

            if (bGeoBlendByFacingDirection3 && bVertexBlendByFacingDirection3)
            {
                float flDirectionMultiplier = fma(dot(g_vFacingDirectionNormalizedSafe3.xyz, vNormalOut.xyz), 0.5, 0.5);
                flDirectionMultiplier = smoothstep(g_vFacingDirectionMinMax3.x, g_vFacingDirectionMinMax3.y, flDirectionMultiplier);

                vColorBlendValues.y *= flDirectionMultiplier;
            }
        #endif

        vColorBlendValues.a = clamp(vColorBlendValues.a + flSoftness, 0.001, 1.0);
    #endif

    vCentroidNormalOut = vNormalOut;
}
