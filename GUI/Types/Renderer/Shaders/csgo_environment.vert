#version 460

#if !(defined(csgo_environment) || defined(csgo_environment_blend))
    #error "This shader is not supported!"
#endif

#if defined(csgo_environment)
#include "common/animation.glsl"
#endif

#include "common/compression.glsl"

#include "common/utils.glsl"

#include "common/features.glsl"
#include "csgo_environment.features"

layout (location = 0) in vec3 vPOSITION;
in vec4 vNORMAL;
in vec2 vTEXCOORD;

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

#if (D_COMPRESSED_NORMALS_AND_TANGENTS == 0)
    in vec3 vTANGENT;
#endif

#if defined(csgo_environment_blend)
    // real semantic index is 4
    in vec4 vTEXCOORD3;
    out vec4 vColorBlendValues;
#endif


#if (F_PAINT_VERTEX_COLORS == 1)
    in vec4 vCOLOR;
#endif

out vec3 vFragPosition;

out vec3 vNormalOut;
centroid out vec3 vCentroidNormalOut;
out vec3 vTangentOut;
out vec3 vBitangentOut;

out vec4 vTexCoord;
out vec4 vVertexColor;

uniform vec4 m_vTintColorSceneObject;
uniform vec3 m_vTintColorDrawCall;
uniform vec4 g_vColorTint = vec4(1.0);
uniform float g_flModelTintAmount = 1.0;
uniform float g_flFadeExponent = 1.0;

#include "common/ViewConstants.glsl"
#include "common/LightingConstants.glsl"
uniform mat4 transform;

// Material 1
uniform float g_flTexCoordRotation1 = 0.0;
uniform vec4 g_vTexCoordCenter1 = vec4(0.5);
uniform vec4 g_vTexCoordOffset1 = vec4(0.0);
uniform vec4 g_vTexCoordScale1 = vec4(1.0);

// Material 2
#if defined(csgo_environment_blend)
    uniform float g_flTexCoordRotation2 = 0.0;
    uniform vec4 g_vTexCoordCenter2 = vec4(0.5);
    uniform vec4 g_vTexCoordOffset2 = vec4(0.0);
    uniform vec4 g_vTexCoordScale2 = vec4(1.0);
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
    TintFade.rgb = mix(vec3(1.0), m_vTintColorSceneObject.rgb * m_vTintColorDrawCall * g_vColorTint.rgb, g_flModelTintAmount);
    TintFade.a = pow(m_vTintColorSceneObject.a * g_vColorTint.a, g_flFadeExponent);
    return TintFade;
}

void main()
{
    mat4 skinTransform = transform;
    vec4 fragPosition = skinTransform * vec4(vPOSITION, 1.0);
    gl_Position = g_matViewToProjection * fragPosition;
    vFragPosition = fragPosition.xyz / fragPosition.w;

    mat3 normalTransform = transpose(inverse(mat3(skinTransform)));

    // Unpack normals
#if (D_COMPRESSED_NORMALS_AND_TANGENTS == 0)
    vec3 normal = vNORMAL.xyz;
    vec3 tangent = vTANGENT.xyz;
    vNormalOut = normalize(normalTransform * normal);
    vTangentOut = normalize(normalTransform * tangent);
    vBitangentOut = cross(vNormalOut, vTangentOut);
#else
    vec3 normal = DecompressNormal(vNORMAL);
    vec4 tangent = DecompressTangent(vNORMAL);
    vNormalOut = normalize(normalTransform * normal);
    vTangentOut = normalize(normalTransform * tangent.xyz);
    vBitangentOut = tangent.w * cross( vNormalOut, vTangentOut );
#endif

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

#if (F_PAINT_VERTEX_COLORS == 1)
    vVertexColor *= vCOLOR / 255.0f;
#endif

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

#if defined(csgo_environment_blend)
    vTexCoord.zw = RotateVector2D(vTEXCOORD.xy,
        g_flTexCoordRotation2,
        g_vTexCoordScale2.xy,
        g_vTexCoordOffset2.xy,
        g_vTexCoordCenter2.xy
    );

    vColorBlendValues = vTEXCOORD3 / 255.0f;
#endif

    vCentroidNormalOut = vNormalOut;
}
