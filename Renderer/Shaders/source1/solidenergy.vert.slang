#version 460

#include "solidenergy_features.slang"
#include "common_vs.slang"

in vec3 vNORMAL;
in vec4 vTANGENT;

// $detail1texturetransform and $detail2texturetransform, with the scale of the detail applied
uniform vec4 g_vDetail1TransformRow0 = vec4(1.0, 0.0, 0.0, 0.0);
uniform vec4 g_vDetail1TransformRow1 = vec4(0.0, 1.0, 0.0, 0.0);
uniform vec4 g_vDetail2TransformRow0 = vec4(1.0, 0.0, 0.0, 0.0);
uniform vec4 g_vDetail2TransformRow1 = vec4(0.0, 1.0, 0.0, 0.0);

out vec3 vFragPosition;
out vec2 vTexCoordOut;
out vec2 vDetail1CoordOut;
out vec2 vDetail2CoordOut;
out vec2 vSurfaceCoordOut;
out vec3 vVertexColorOut;

void main()
{
    mat3x4 objectToWorld = CalculateSkinTransform();

    vFragPosition = vec4(vPOSITION, 1.0) * objectToWorld;
    gl_Position = g_matWorldToProjection * vec4(vFragPosition, 1.0);

    vTexCoordOut = GetSource1BaseTexCoord(vTEXCOORD);
    vDetail1CoordOut = TransformSource1TexCoord(g_vDetail1TransformRow0, g_vDetail1TransformRow1, vTEXCOORD);
    vDetail2CoordOut = TransformSource1TexCoord(g_vDetail2TransformRow0, g_vDetail2TransformRow1, vTEXCOORD);

    // The position along the tangent and the bitangent of the surface, which the flow is laid out in
    vec3 tangent = TransformDirectionToWorld(objectToWorld, vTANGENT.xyz);
    vec3 normal = TransformDirectionToWorld(objectToWorld, vNORMAL);
    vec3 bitangent = cross(normal, tangent) * vTANGENT.w;
    vSurfaceCoordOut = vec2(dot(vFragPosition, tangent), dot(vFragPosition, bitangent));

#if (F_VERTEX_COLOR == 1)
    vVertexColorOut = vCOLOR.rgb;
#else
    vVertexColorOut = vec3(1.0);
#endif
}
