#version 460

#ifndef VRF_SOURCE1_COMMON_VS_INCLUDE
#define VRF_SOURCE1_COMMON_VS_INCLUDE

#include "../common/utils.slang"
#include "../common/view_constants.slang"
#include "../common/animation.slang"

in vec3 vPOSITION;
in vec2 vTEXCOORD;

uniform vec4 g_vTexCoordTransformRow0 = vec4(1.0, 0.0, 0.0, 0.0);
uniform vec4 g_vTexCoordTransformRow1 = vec4(0.0, 1.0, 0.0, 0.0);

uniform vec2 g_vTexCoordScrollSpeed;

uniform int F_TEXTURE_ANIMATION;
uniform vec2 g_vAnimationGrid = vec2(1.0, 1.0);
uniform int g_nNumAnimationCells = 1;
uniform float g_flAnimationTimePerFrame = 0.1;
uniform float g_flAnimationTimeOffset;

uniform vec4 g_vBumpTransformRow0 = vec4(1.0, 0.0, 0.0, 0.0);
uniform vec4 g_vBumpTransformRow1 = vec4(0.0, 1.0, 0.0, 0.0);
#if (SOURCE1_BUMPMAP2 == 1)
uniform vec4 g_vBumpTransform2Row0 = vec4(1.0, 0.0, 0.0, 0.0);
uniform vec4 g_vBumpTransform2Row1 = vec4(0.0, 1.0, 0.0, 0.0);
uniform vec2 g_vBump2ScrollSpeed;
#endif
uniform vec2 g_vBumpScrollSpeed;
uniform vec4 g_vEnvMapMaskTransformRow0 = vec4(1.0, 0.0, 0.0, 0.0);
uniform vec4 g_vEnvMapMaskTransformRow1 = vec4(0.0, 1.0, 0.0, 0.0);

#if (F_VERTEX_COLOR == 1 || F_VERTEX_ALPHA == 1 || SOURCE1_BLEND_BY_VERTEX_ALPHA == 1)
    in vec4 vCOLOR;
#endif

// The entity render color and amount, multiplied by the vertex color (F_VERTEX_COLOR, rgb) and vertex alpha
// (F_VERTEX_ALPHA, a) when the shader asks for them
vec4 GetSource1VertexColor()
{
    vec4 color = GetObjectData().vTint;

#if (F_VERTEX_COLOR == 1)
    color.rgb *= vCOLOR.rgb;
#endif

#if (F_VERTEX_ALPHA == 1)
    color.a *= vCOLOR.a;
#endif

    return color;
}

vec4 TransformTangentToWorld(mat3x4 objectToWorld, vec4 tangent)
{
    return vec4(TransformDirectionToWorld(objectToWorld, tangent.xyz), tangent.w);
}

// A texture transform of a material: u' = row0 . (u, v, 0, 1), v' = row1 . (u, v, 0, 1)
vec2 TransformSource1TexCoord(vec4 row0, vec4 row1, vec2 texCoord)
{
    vec4 position = vec4(texCoord, 0.0, 1.0);
    return vec2(dot(row0, position), dot(row1, position));
}

// $basetexturetransform, then the cell of the texture sheet or the scroll of the material
vec2 GetSource1BaseTexCoord(vec2 texCoord)
{
    vec2 transformed = TransformSource1TexCoord(g_vTexCoordTransformRow0, g_vTexCoordTransformRow1, texCoord);

    if (F_TEXTURE_ANIMATION > 0)
    {
        uint frame = uint((g_flAnimationTimeOffset + g_flTime) / g_flAnimationTimePerFrame) % uint(g_nNumAnimationCells);
        uint columns = uint(g_vAnimationGrid.x);
        vec2 cell = vec2(float(frame % columns), float(frame / columns));
        return (transformed + cell) / g_vAnimationGrid.xy;
    }

    return transformed + g_vTexCoordScrollSpeed * g_flTime;
}

// $bumptransform
vec2 GetSource1BumpTexCoord(vec2 texCoord)
{
    return TransformSource1TexCoord(g_vBumpTransformRow0, g_vBumpTransformRow1, texCoord) + g_vBumpScrollSpeed * g_flTime;
}

#if (SOURCE1_BUMPMAP2 == 1)
// $bumptransform2
vec2 GetSource1BumpTexCoord2(vec2 texCoord)
{
    return TransformSource1TexCoord(g_vBumpTransform2Row0, g_vBumpTransform2Row1, texCoord) + g_vBump2ScrollSpeed * g_flTime;
}
#endif

// $envmapmasktransform
vec2 GetSource1EnvMapMaskTexCoord(vec2 texCoord)
{
    return TransformSource1TexCoord(g_vEnvMapMaskTransformRow0, g_vEnvMapMaskTransformRow1, texCoord);
}

#endif
