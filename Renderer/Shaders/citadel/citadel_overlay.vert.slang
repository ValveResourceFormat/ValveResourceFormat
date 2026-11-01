#version 460

#include "citadel_overlay_features.slang"

uniform vec3 g_vColorTint = vec3(1.0);

#include "../common/surface_base_vs.slang"
#include "../common/skinned_vertex.slang"

#if (F_DECAL_SPACE_NORMALS == 1)
    in vec4 vTEXCOORD5; // OverlayProjectionDirection
    out vec4 vProjectionDir;
#endif

void main()
{
    ObjectData_t object = GetObjectData();

    WriteSkinnedPositionAndTangentFrame();
    vCentroidNormalOut = vNormalOut;

    WriteBakedLighting();

    vTexCoordOut = vTEXCOORD.xy;

    vVertexColorOut.rgb = SrgbGammaToLinear(object.vTint.rgb);
    vVertexColorOut.a = object.vTint.a;

#if (F_DECAL_SPACE_NORMALS == 1)
    vProjectionDir = UnpackFromColor(vTEXCOORD5);
#endif
}
