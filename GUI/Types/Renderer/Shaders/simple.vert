#version 460

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
#define F_PAINT_VERTEX_COLORS 0 // csgo_static_overlay
#define F_LAYERS 0
#define F_SECONDARY_UV 0
#define F_FORCE_UV2 0
#define F_DETAIL_TEXTURE 0
#define F_FOLIAGE_ANIMATION 0
#define F_TEXTURE_ANIMATION 0
#define F_TEXTURE_ANIMATION_MODE 0
#define F_SPHERICAL_PROJECTED_ANISOTROPIC_TANGENTS 0
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

#if (F_SECONDARY_UV == 1) || (F_FORCE_UV2 == 1)
    in vec4 vTEXCOORD2;
    out vec2 vTexCoord2;
#endif

#include "common/LightingConstants.glsl"

#if D_BAKED_LIGHTING_FROM_LIGHTMAP == 1
    in vec2 vLightmapUV;
    out vec3 vLightmapUVScaled;
#elif D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1
    in vec4 vPerVertexLighting;
    out vec4 vPerVertexLightingOut;
#endif

#if (D_COMPRESSED_NORMALS_AND_TANGENTS == 0)
    in vec3 vTANGENT;
#endif

#if (F_LAYERS > 0) || defined(simple_2way_blend) || defined(vr_blend)
    #if defined(vr_simple_2way_blend) || defined(vr_blend)
        #define vBLEND_COLOR vTEXCOORD2
        #if defined(vr_blend)
            #define vBLEND_ALPHA vTEXCOORD3
            in vec4 vBLEND_ALPHA;
        #endif
    #else
        // lightmappedgeneric, csgo_simple_2way_blend
        // real semantic index is 4
        #define vBLEND_COLOR vTEXCOORD3
    #endif
    in vec4 vBLEND_COLOR;
    out vec4 vColorBlendValues;
#endif

#if (F_VERTEX_COLOR == 1) || (F_PAINT_VERTEX_COLORS == 1)
    in vec4 vCOLOR;
#endif

#if (F_FOLIAGE_ANIMATION > 0)
    in vec4 vTEXCOORD1;
    #if !((F_SECONDARY_UV == 1) || (F_FORCE_UV2 == 1))
    in vec4 vTEXCOORD2;
    #endif
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
uniform vec4 g_vTexCoordCenter = vec4(0.5);
uniform float g_flTexCoordRotation = 0.0;

#if F_TEXTURE_ANIMATION == 1
    uniform vec4 g_vAnimationGrid = vec4(1, 1, 0, 0);
    uniform int g_nNumAnimationCells = 1;
    uniform float g_flAnimationTimePerFrame;
    uniform float g_flAnimationTimeOffset;
    uniform float g_flAnimationFrame;
#endif

#if (F_SPHERICAL_PROJECTED_ANISOTROPIC_TANGENTS == 1)
uniform float g_vSphericalAnisotropyAngle = 180.0;
uniform vec4 g_vSphericalAnisotropyPole = vec4(0, 0, 1, 0);

out vec3 vAnisoBitangentOut;

vec3 GetSphericalProjectedAnisoBitangent(vec3 normal, vec3 tangent)
{
    float angle = (g_vSphericalAnisotropyAngle / 90.0) - 1.0;

    vec3 vAnisoTangent = cross(g_vSphericalAnisotropyPole.xyz, normal);
    // Prevent length of 0
    vAnisoTangent = mix(vAnisoTangent, tangent, bvec3(length(vec3(equal(vAnisoTangent, vec3(0.0)))) != 0.0));

    if (g_vSphericalAnisotropyAngle != 0.0)
    {
        vec3 rotated1 = mix(cross(normal, vAnisoTangent), vAnisoTangent, ClampToPositive(g_vSphericalAnisotropyAngle));
        vAnisoTangent = mix(rotated1, -vAnisoTangent, ClampToPositive(-g_vSphericalAnisotropyAngle));
    }
    return vAnisoTangent;
}
#endif

vec2 GetAnimatedUVs(vec2 texCoords)
{
    #if F_TEXTURE_ANIMATION == 1
        uint frame = uint(g_flAnimationFrame);
        uint cells = uint(g_nNumAnimationCells);
        #if F_TEXTURE_ANIMATION_MODE == 0 // Sequential
            frame = uint((g_flAnimationTimeOffset + g_flTime) / g_flAnimationTimePerFrame) % cells;
        #elif F_TEXTURE_ANIMATION_MODE == 1 // Random
            frame = uint(Random2D(vec2(g_flAnimationFrame)) * float(g_nNumAnimationCells));
        #endif

        vec2 atlasGridInv = vec2(1.0) / g_vAnimationGrid.xy;
        vec2 atlasOffset = vec2(uvec2(
            frame % uint(g_vAnimationGrid.x),
            uint(float(frame) * atlasGridInv.x)
        )) * atlasGridInv;

        texCoords = texCoords * atlasGridInv + atlasOffset;
    #endif

    texCoords = RotateVector2D(texCoords, g_flTexCoordRotation, g_vTexCoordScale.xy, g_vTexCoordOffset.xy, g_vTexCoordCenter.xy);
    return texCoords + g_vTexCoordScrollSpeed.xy * g_flTime;
    //return texCoords * g_vTexCoordScale.xy + g_vTexCoordOffset.xy + (g_vTexCoordScrollSpeed.xy * g_flTime);
}

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

#if (F_SECONDARY_UV == 1) || (F_FORCE_UV2 == 1)
    uniform bool g_bUseSecondaryUvForDetailTexture = false; // this doesn't always show up
#endif

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

#if (F_SPHERICAL_PROJECTED_ANISOTROPIC_TANGENTS == 1)
    vAnisoBitangentOut = normalTransform * GetSphericalProjectedAnisoBitangent(normal, tangent.xyz);
#endif

#if (F_FOLIAGE_ANIMATION > 0) && !((F_SECONDARY_UV == 1) || (F_FORCE_UV2 == 1))
    // TODO: this should always be texcoord semanticindex 0
	vTexCoordOut = GetAnimatedUVs(vTEXCOORD2.xy);
#else
	vTexCoordOut = GetAnimatedUVs(vTEXCOORD.xy);
#endif

#if D_BAKED_LIGHTING_FROM_LIGHTMAP == 1
    vLightmapUVScaled = vec3(vLightmapUV * g_vLightmapUvScale.xy, 0);
#elif D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1
    vec3 Light = vPerVertexLighting.rgb * 6.0 * vPerVertexLighting.a;
    vPerVertexLightingOut = pow2(Light);
#endif

    vVertexColorOut = GetTintColor();

#if F_PAINT_VERTEX_COLORS == 1
    vVertexColorOut *= vCOLOR / 255.0f;
#endif

#if (F_SECONDARY_UV == 1) || (F_FORCE_UV2 == 1)
    vTexCoord2 = vTEXCOORD2.xy;
#endif

#if F_DETAIL_TEXTURE > 0
    #if (F_SECONDARY_UV == 1) || (F_FORCE_UV2 == 1)
        vec2 detailCoords = (g_bUseSecondaryUvForDetailTexture || (F_FORCE_UV2 == 1)) ? vTexCoord2 : vTexCoordOut;
    #else
        #define detailCoords vTexCoordOut
    #endif
    vDetailTexCoords = RotateVector2D(detailCoords, g_flDetailTexCoordRotation, g_vDetailTexCoordScale.xy, g_vDetailTexCoordOffset.xy);
#endif

#if (F_LAYERS > 0) || defined(simple_2way_blend) || defined(vr_blend)
    vColorBlendValues = vBLEND_COLOR;

    #if defined(csgo_simple_2way_blend) || (F_LAYERS > 0)
        vColorBlendValues /= 255.0;
    #endif

    #if defined(vr_blend)
        vColorBlendValues.y = max(0.5 * vBLEND_ALPHA.x, 0.1);
    #endif
#endif

    vCentroidNormalOut = vNormalOut;
}
