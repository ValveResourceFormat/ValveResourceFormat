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

#if (F_VERTEX_COLOR == 1 || F_VERTEX_ALPHA == 1 || SOURCE1_BLEND_BY_VERTEX_ALPHA == 1)
    in vec4 vCOLOR;
#endif

// White unless the shader asks for the vertex color (F_VERTEX_COLOR, rgb) or vertex alpha (F_VERTEX_ALPHA, a)
vec4 GetSource1VertexColor()
{
    vec4 color = vec4(1.0);

#if (F_VERTEX_COLOR == 1)
    color.rgb = vCOLOR.rgb;
#endif

#if (F_VERTEX_ALPHA == 1)
    color.a = vCOLOR.a;
#endif

    return color;
}

vec4 TransformTangentToWorld(mat3x4 objectToWorld, vec4 tangent)
{
    return vec4(TransformDirectionToWorld(objectToWorld, tangent.xyz), tangent.w);
}

// $basetexturetransform: u' = row0 . (u, v, 0, 1), v' = row1 . (u, v, 0, 1)
vec2 GetSource1BaseTexCoord(vec2 texCoord)
{
    vec4 position = vec4(texCoord, 0.0, 1.0);
    return vec2(dot(g_vTexCoordTransformRow0, position), dot(g_vTexCoordTransformRow1, position));
}

#endif
