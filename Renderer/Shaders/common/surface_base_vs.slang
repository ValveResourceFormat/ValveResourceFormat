#version 460

#ifndef VRF_SURFACE_BASE_VS_INCLUDE
#define VRF_SURFACE_BASE_VS_INCLUDE

// Inputs, outputs and helpers of a lit surface vertex shader. The shader declares its combos and g_vColorTint first,
// includes this, and writes main() from the helpers. It reads F_USE_PER_VERTEX_CURVATURE, F_SECONDARY_UV and
// F_SPHERICAL_PROJECTED_ANISOTROPIC_TANGENTS, so it declares those combos.
// g_vColorTint is declared by the shader because materials store it with or without sRGB encoding.

#include "utils.slang"
#include "shared_combos.slang"
#include "view_constants.slang"
#include "lighting_constants.slang"

#include "instancing.slang"
#include "animation.slang"
#include "morph.slang"

in vec3 vPOSITION;
in vec2 vTEXCOORD;
#include "compression.slang"

uniform int F_NOTINT;
uniform int F_TEXTURE_ANIMATION;
uniform int F_TEXTURE_ANIMATION_MODE;

#if (F_USE_PER_VERTEX_CURVATURE == 1)
    in float flSSSCurvature;
    out float vCurvatureOut;
#endif

#include "baked_lighting_vs.slang"

#if (F_SECONDARY_UV == 1)
    in vec4 vTEXCOORD1;
    out vec2 vTexCoord2;
#endif

centroid out vec4 vVertexColorOut;
out vec3 vFragPosition;
out vec3 vNormalOut;
out vec4 vTangentOut;
centroid out vec3 vCentroidNormalOut;
out vec2 vTexCoordOut;

uniform vec2 g_vTexCoordOffset;
uniform vec2 g_vTexCoordScale = vec2(1.0);
uniform vec2 g_vTexCoordScrollSpeed;
uniform vec2 g_vTexCoordCenter = vec2(0.5);
uniform float g_flTexCoordRotation = 0.0;

uniform vec2 g_vAnimationGrid = vec2(1, 1);
uniform int g_nNumAnimationCells = 1;
uniform float g_flAnimationTimePerFrame = 0.1;
uniform float g_flAnimationTimeOffset;
uniform float g_flAnimationFrame;

#if (F_SPHERICAL_PROJECTED_ANISOTROPIC_TANGENTS == 1)
    uniform float g_vSphericalAnisotropyAngle = 180.0;
    uniform vec3 g_vSphericalAnisotropyPole = vec3(0, 0, 1);

    out vec3 vAnisoBitangentOut;

    vec3 GetSphericalProjectedAnisoBitangent(vec3 normal, vec3 tangent)
    {
        float angle = (g_vSphericalAnisotropyAngle / 90.0) - 1.0;

        vec3 vAnisoTangent = cross(g_vSphericalAnisotropyPole.xyz, normal);
        // Prevent length of 0
        vAnisoTangent = mix(vAnisoTangent, tangent, bvec3(any(equal(vAnisoTangent, vec3(0.0)))));

        if (g_vSphericalAnisotropyAngle != 0.0)
        {
            vec3 rotated1 = mix(cross(normal, vAnisoTangent), vAnisoTangent, ClampToPositive(g_vSphericalAnisotropyAngle));
            vAnisoTangent = mix(rotated1, -vAnisoTangent, ClampToPositive(-g_vSphericalAnisotropyAngle));
        }
        return vAnisoTangent;
    }
#endif

// The texture animation atlas and scroll, for coordinates the shader transformed itself.
// Texture animation atlases scroll with g_vTexCoordScrollSpeed too when scrollsAnimatedAtlas is set.
vec2 ApplyTextureAnimation(vec2 texCoords, bool scrollsAnimatedAtlas)
{
    if (F_TEXTURE_ANIMATION > 0)
    {
        // Shaders without a mode combo pick random frames with F_TEXTURE_ANIMATION 2
        int mode = F_TEXTURE_ANIMATION == 2 ? 1 : F_TEXTURE_ANIMATION_MODE;

        uint frame = uint(g_flAnimationFrame);
        uint cells = uint(g_nNumAnimationCells);
        if (mode == 0) // Sequential
            frame = uint((g_flAnimationTimeOffset + g_flTime) / g_flAnimationTimePerFrame) % cells;
        else if (mode == 1) // Random
            frame = uint(Random2D(vec2(float(uint(g_flAnimationFrame)))) * float(g_nNumAnimationCells));

        vec2 atlasGridInv = vec2(1.0) / g_vAnimationGrid.xy;
        vec2 atlasOffset = vec2(uvec2(
            frame % uint(g_vAnimationGrid.x),
            uint(float(frame) * atlasGridInv.x)
        )) * atlasGridInv;

        texCoords = texCoords * atlasGridInv + atlasOffset;

        if (!scrollsAnimatedAtlas)
        {
            return texCoords;
        }
    }

    return texCoords + g_vTexCoordScrollSpeed.xy * g_flTime;
}

vec2 GetAnimatedUVs(vec2 texCoords, bool scrollsAnimatedAtlas)
{
    // The scale applies around the UV origin, only the rotation pivots on the center
    vec2 scaledTexCoords = g_vTexCoordScale.xy * texCoords;
    texCoords = RotateVector2D(scaledTexCoords - g_vTexCoordCenter.xy, g_flTexCoordRotation) + g_vTexCoordCenter.xy + g_vTexCoordOffset.xy;

    return ApplyTextureAnimation(texCoords, scrollsAnimatedAtlas);
}

vec4 GetTintColorLinear(vec4 vTint, float modelTintAmount, float fadeExponent)
{
    vec4 TintFade = vec4(1.0);
    if (F_NOTINT == 0)
    {
        TintFade.rgb = mix(vec3(1.0), SrgbGammaToLinear(vTint.rgb), modelTintAmount) * g_vColorTint.rgb;
    }
    TintFade.a = pow(vTint.a, max(fadeExponent, 0.0001));
    return TintFade;
}

uniform int g_bFirstpersonLegsDistortion = 0;

// Fish-eye warp of first-person legs, applied to clip space position and normal.
// Scales outward and pulls back so legs do not clip when looking down.
void ApplyFirstpersonLegsDistortion(
    in vec3 vertexPosOS,
    inout vec4 clipSpacePos,
    inout vec3 normal,
    in float viewDirZ)
{
    // Vertex Z height (normalized, clamped) times camera pitch looking down, squared for sharper falloff
    float heightFactor = saturate((vertexPosOS.z + 1.0) * 0.0125) * 0.5;
    float pitchFactor = pow2(saturate(-viewDirZ));
    float distortionAmount = pow(heightFactor * pitchFactor, 1.5);

    // Apply distortion to clip space position
    vec2 scaledXY = clipSpacePos.xy * (1.0 + (5.0 * distortionAmount)); // Scale XY outward by up to 5x
    float offsetY = scaledXY.y - (200.0 * distortionAmount); // Pull down in Y
    float offsetZ = clipSpacePos.z;

    clipSpacePos = vec4(scaledXY.x, offsetY, offsetZ, clipSpacePos.w);

    // Adjust normal to point slightly upward when distortion is applied
    normal = normalize(normal + (vec3(0.0, 0.0, 0.5) * sign(distortionAmount)));
}

// Writes the outputs that every lit surface vertex has. The shader multiplies its vertex paint into vVertexColorOut
// and writes its own outputs afterwards.
void WriteSurfaceVertex(bool scrollsAnimatedAtlas, float modelTintAmount, float fadeExponent)
{
    ObjectData_t object = GetObjectData();

    mat3x4 skinTransform = CalculateSkinTransform();
    vFragPosition = vec4(vPOSITION + GetMorphOffset(), 1.0) * skinTransform;

    vec3 normal;
    vec4 tangent;
    GetOptionallyCompressedNormalTangent(normal, tangent);
    normal = ApplyMorphNormal(normal);
    ComputeWorldTangentFrame(skinTransform, normal, tangent, vNormalOut, vTangentOut);

    gl_Position = g_matWorldToProjection * vec4(vFragPosition, 1.0);

    if (g_bFirstpersonLegsDistortion != 0)
    {
        ApplyFirstpersonLegsDistortion(vPOSITION, gl_Position, vNormalOut, g_vCameraDirWs.z);
    }

#if (F_SPHERICAL_PROJECTED_ANISOTROPIC_TANGENTS == 1)
    vAnisoBitangentOut = TransformDirectionToWorld(skinTransform, GetSphericalProjectedAnisoBitangent(normal, tangent.xyz));
#endif

    vTexCoordOut = GetAnimatedUVs(vTEXCOORD.xy, scrollsAnimatedAtlas);

#if (F_USE_PER_VERTEX_CURVATURE == 1)
    vCurvatureOut = flSSSCurvature;
#endif

    WriteBakedLighting();

    vVertexColorOut = GetTintColorLinear(object.vTint, modelTintAmount, fadeExponent);

#if (F_SECONDARY_UV == 1)
    vTexCoord2 = vTEXCOORD1.xy;
#endif

    vCentroidNormalOut = vNormalOut;
}

// Vertex paint that is multiplied without sRGB decode, where all-zero paint means unpainted
vec4 GetUndecodedVertexPaint(vec4 paint)
{
    return mix(vec4(1.0), paint, sign(dot(paint, vec4(1.0))));
}

#endif
