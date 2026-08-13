#version 460

#include "common/instancing.slang"
#include "common/animation.slang"

//Parameter defines - These are default values and can be overwritten based on material/model parameters
#define F_PAINT_VERTEX_COLORS 0 // citycrawl_building_anim, csgo_complex, csgo_depth_only, csgo_glass, csgo_simple, csgo_simple_2way_blend, csgo_static_overlay, generic, global_lit_boardspray, global_lit_erosion, global_lit_simple, liquid_fx, pbr
//End of parameter defines

in vec3 vPOSITION;
in vec2 vTEXCOORD;
#include "common/compression.slang"

#if (F_PAINT_VERTEX_COLORS == 1)
    in vec4 vTEXCOORD2;
    out vec4 vVertexColorOut;
#endif

out vec3 vFragPosition;
out vec3 vNormalOut;
out vec3 vTangentOut;
out vec3 vBitangentOut;
out vec2 vTexCoordOut;
flat out vec4 vTintColorFadeOut;

uniform vec2 g_vTexCoordOffset = vec2(0.0);
uniform vec2 g_vTexCoordScale = vec2(1.0);

#include "common/ViewConstants.slang"

void main()
{
    ObjectData_t object = GetObjectData();

    mat3x4 skinTransform = CalculateSkinTransform();
    vec3 fragPosition = vec4(vPOSITION, 1.0) * skinTransform;
    gl_Position = g_matWorldToProjection * vec4(fragPosition, 1.0);
    vFragPosition = fragPosition;

    vec3 normal;
    vec4 tangent;
    GetOptionallyCompressedNormalTangent(normal, tangent);

    vNormalOut = TransformDirectionToWorld(skinTransform, normal);
    vTangentOut = TransformDirectionToWorld(skinTransform, tangent.xyz);
    vBitangentOut = tangent.w * cross(vNormalOut, vTangentOut);

    vTintColorFadeOut = object.vTint;
    vTexCoordOut = vTEXCOORD * g_vTexCoordScale.xy + g_vTexCoordOffset.xy;

    #if (F_PAINT_VERTEX_COLORS == 1)
        vVertexColorOut = vTEXCOORD2;
    #endif
}
