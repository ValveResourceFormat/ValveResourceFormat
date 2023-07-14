#version 330

//Includes - resolved by VRF
#include "compression.incl"
#include "animation.incl"
#include "common/utils.glsl"
//End of includes

//Parameter defines - These are default values and can be overwritten based on material/model parameters
#define D_BAKED_LIGHTING_FROM_LIGHTMAP 0
#define D_BAKED_LIGHTING_FROM_VERTEX_STREAM 0
#define D_BAKED_LIGHTING_FROM_LIGHTPROBE 0

#define F_NOTINT 0
#define F_VERTEX_COLOR 0
#define F_LAYERS 0
#define F_SECONDARY_UV 0
#define F_DETAIL_TEXTURE 0
//End of parameter defines

#if defined(vr_simple_2way_blend) || defined (csgo_simple_2way_blend)
    #define simple_2way_blend
#endif
#if defined(vr_simple_2way_blend) || defined(vr_simple_2way_parallax) || defined(vr_simple_3way_parallax) || defined(vr_simple_blend_to_triplanar) || defined(vr_simple_blend_to_xen_membrane)
    #define vr_blend
#endif

layout (location = 0) in vec3 vPOSITION;
in vec4 vNORMAL;
in vec2 vTEXCOORD;

#if D_BAKED_LIGHTING_FROM_LIGHTMAP == 1
    in vec2 vLightmapUV;
    out vec3 vLightmapUVScaled;
    uniform vec2 g_vLightmapUvScale;
#elif D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1
    in vec4 vPerVertexLighting;
    out vec4 vPerVertexLightingOut;
#endif

#if (F_LAYERS > 0) || defined(simple_2way_blend)
    #if defined(simple_2way_blend)
        #define vBLEND_COLOR vTEXCOORD2
        #if defined(vr_blend)
            #define vBLEND_ALPHA vTEXCOORD3
            in vec4 vBLEND_ALPHA;
        #endif
    #else
        // ligthtmappedgeneric - real semantic index is 4
        #define vBLEND_COLOR vTEXCOORD3
    #endif
    in vec4 vBLEND_COLOR;
    out vec4 vColorBlendValues;
#endif
#if (D_COMPRESSED_NORMALS_AND_TANGENTS == 0)
    in vec3 vTANGENT;
#endif
#if F_VERTEX_COLOR == 1
    in vec4 vCOLOR;
#endif
#if F_SECONDARY_UV
    in vec2 vTEXCOORD2;
    out vec2 vTexCoord2;
#endif

out vec4 vVertexColorOut;
out vec3 vFragPosition;

out vec3 vNormalOut;
centroid out vec3 vCentroidNormalOut;
out vec3 vTangentOut;
out vec3 vBitangentOut;

out vec2 vTexCoordOut;

uniform vec4 m_vTintColorSceneObject;
uniform vec3 m_vTintColorDrawCall;
uniform vec4 g_vColorTint = vec4(1.0);
uniform float g_flModelTintAmount = 1.0;
uniform float g_flFadeExponent = 1.0;

uniform mat4 uProjectionViewMatrix;
uniform mat4 transform;

uniform vec4 g_vTexCoordOffset;
uniform vec4 g_vTexCoordScale = vec4(1.0);
uniform vec4 g_vTexCoordScrollSpeed;
uniform float g_flTime;


vec4 GetTintColor()
{
    vec4 TintFade = vec4(1.0);
#if F_NOTINT == 0
    TintFade.rgb = mix(vec3(1.0), m_vTintColorSceneObject.rgb * m_vTintColorDrawCall * g_vColorTint.rgb, g_flModelTintAmount);
#endif
    TintFade.a = pow(m_vTintColorSceneObject.a * g_vColorTint.a, g_flFadeExponent);
    return TintFade;
}

#if (F_DETAIL_TEXTURE > 0)
    uniform float g_flDetailTexCoordRotation = 0.0;
    uniform vec4 g_vDetailTexCoordOffset = vec4(0.0);
    uniform vec4 g_vDetailTexCoordScale = vec4(1.0);
    out vec2 vDetailTexCoords;

    #if (F_SECONDARY_UV == 1)
        uniform bool g_bUseSecondaryUvForDetailTexture = false; // this doesn't always show up
    #endif

    vec2 getDetailCoords(vec2 texCoords)
    {
        // in S2 most of these values are precomputed into the globals buffer
        float rotation = radians(g_flDetailTexCoordRotation);
        float sinRot = sin(rotation);
        float cosRot = cos(rotation);
        vec2 offset = (g_vDetailTexCoordScale.xy * -0.5) * vec2(cosRot - sinRot, sinRot - cosRot) + g_vDetailTexCoordOffset.xy + 0.5;
        vec4 xform = g_vDetailTexCoordScale.xxyy * vec4(cosRot, -sinRot, sinRot, cosRot);

        return vec2(dot(xform.xy, texCoords.xy), dot(xform.zw, texCoords.xy)) + offset;
    }
#endif


void main()
{
    mat4 skinTransform = transform * getSkinMatrix();
    vec4 fragPosition = skinTransform * vec4(vPOSITION, 1.0);
    gl_Position = uProjectionViewMatrix * fragPosition;
    vFragPosition = fragPosition.xyz / fragPosition.w;

    mat3 normalTransform = transpose(inverse(mat3(skinTransform)));

    //Unpack normals
#if (D_COMPRESSED_NORMALS_AND_TANGENTS == 0)
    vNormalOut = normalize(normalTransform * vNORMAL.xyz);
    vTangentOut = normalize(normalTransform * vTANGENT.xyz);
    vBitangentOut = cross(vNormalOut, vTangentOut);
#else
    vec4 tangent = DecompressTangent(vNORMAL);
    vNormalOut = normalize(normalTransform * DecompressNormal(vNORMAL));
    vTangentOut = normalize(normalTransform * tangent.xyz);
    vBitangentOut = tangent.w * cross( vNormalOut, vTangentOut );
#endif

	vTexCoordOut = vTEXCOORD * g_vTexCoordScale.xy + g_vTexCoordOffset.xy + (g_vTexCoordScrollSpeed.xy * g_flTime);

#if D_BAKED_LIGHTING_FROM_LIGHTMAP == 1
    vLightmapUVScaled = vec3(vLightmapUV * g_vLightmapUvScale, 0);
#elif D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1
    //vec3 Light = vPerVertexLighting.rgb * 6.0 * vPerVertexLighting.a;
    //vPerVertexLightingOut = pow2(Light);
    vPerVertexLightingOut = vPerVertexLighting;
#endif

    vVertexColorOut = GetTintColor();

#if F_VERTEX_COLOR == 1
    //vVertexColorOut *= vCOLOR;
#endif


#if F_SECONDARY_UV == 1
    vTexCoord2 = vTEXCOORD2;
#endif

#if F_DETAIL_TEXTURE > 0
    #if F_SECONDARY_UV == 1
        vDetailTexCoords = getDetailCoords(g_bUseSecondaryUvForDetailTexture ? vTexCoord2 : vTexCoordOut);
    #else
        vDetailTexCoords = getDetailCoords(vTexCoordOut);
    #endif
#endif

#if (F_LAYERS > 0) || defined(simple_2way_blend)
    vColorBlendValues = vBLEND_COLOR / 255.0f;
    // After HLA they presumably realized this was dumb as hell
    #if defined(vr_blend)
        vColorBlendValues.y = max(0.5 * vBLEND_ALPHA.x, 0.1);
    #endif
#endif

    vCentroidNormalOut = vNormalOut;
}
