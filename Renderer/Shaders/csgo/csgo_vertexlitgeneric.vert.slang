#version 460

#include "csgo_vertexlitgeneric_features.slang"

uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)

#include "../common/surface_base_vs.slang"
#include "../common/surface_model_tint_vs.slang"
#include "../features/detail_texture_vs.slang"

uniform vec2 g_vNormalTexCoordScale = vec2(1.0);
uniform vec2 g_vNormalTexCoordOffset = vec2(0.0);
uniform vec2 g_vNormalTexCoordCenter = vec2(0.5);
uniform float g_flNormalTexCoordRotation = 0.0;

out vec2 vMeshTexCoordOut;
out vec2 vNormalTexCoordOut;

void main()
{
    WriteSurfaceVertex(true);

    // The texcoord transform only applies to the color and the masks:
    // the normal map, detail and occlusion are sampled from the mesh texcoords
    vMeshTexCoordOut = vTEXCOORD.xy;
    vNormalTexCoordOut = TransformSecondaryTexCoord(vTEXCOORD.xy, g_flNormalTexCoordRotation, g_vNormalTexCoordScale, g_vNormalTexCoordOffset, g_vNormalTexCoordCenter);

    bool forceUv2 = (F_FORCE_UV2 == 1);
    WriteDetailTexCoordsFromMesh(forceUv2);
}
