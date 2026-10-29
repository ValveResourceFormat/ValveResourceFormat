#version 460

// Vertex shader of the environment shaders. The shader includes its features file first and defines:
// PAINTED_TINT, which is 1 when the vertex paint decides how much of the model tint applies,
// OBJECT_SPACE_POSITION, which is 1 when the fragment shader projects from the object space position.

#include "../common/utils.slang"
#include "../common/shared_combos.slang"

#include "../common/instancing.slang"
#include "../common/animation.slang"

in vec3 vPOSITION;
in vec2 vTEXCOORD;
#include "../common/compression.slang"

in vec2 vTEXCOORD1;
in vec4 vTEXCOORD4; // VertexPaintBlendParams
in vec4 vCOLOR;     // VertexPaintTintColor

out vec3 vFragPosition;
out vec3 vNormalOut;
out vec4 vTangentOut;
centroid out vec3 vCentroidNormalOut;
out vec2 vTexCoordOut;
out vec2 vTexCoord2;
centroid out vec4 vVertexColorOut;
out vec3 vColorBlendValues; // layer 2 and 3 reveal in xy, environment_simple's roughness, metalness and self illumination in xyz

#if (OBJECT_SPACE_POSITION)
    out vec3 vPositionOs;
#endif

#include "../common/view_constants.slang"
#include "../common/lighting_constants.slang"
#include "../common/baked_lighting_vs.slang"
#include "../common/skinned_vertex.slang"
#include "../common/texcoord_scale_by_model.slang"

void main()
{
    ObjectData_t object = GetObjectData();

    WriteSkinnedPositionAndTangentFrame();
    vCentroidNormalOut = vNormalOut;

    WriteBakedLighting();

    vec4 tint = vec4(SrgbGammaToLinear(object.vTint.rgb), object.vTint.a);
    #if (PAINTED_TINT)
        // How much of the model tint applies is painted per vertex.
        tint = mix(vec4(1.0), tint, vTEXCOORD4.w);
    #endif

    vec3 vertexPaint = mix(vec3(1.0), SrgbGammaToLinear(vCOLOR.rgb), vCOLOR.a);
    vVertexColorOut = vec4(tint.rgb * vertexPaint, tint.a);

    vec4 texCoords = ApplyTexCoordScaleByModel(vec4(vTEXCOORD.xy, vTEXCOORD1.xy), CalculateObjectToWorldMatrix());
    vTexCoordOut = texCoords.xy;
    vTexCoord2 = texCoords.zw;
    vColorBlendValues = vTEXCOORD4.xyz;

    #if (OBJECT_SPACE_POSITION)
        vPositionOs = vPOSITION;
    #endif
}
