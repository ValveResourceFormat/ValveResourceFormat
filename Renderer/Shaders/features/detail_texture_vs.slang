#version 460

#ifndef VRF_DETAIL_TEXTURE_VS_INCLUDE
#define VRF_DETAIL_TEXTURE_VS_INCLUDE

// Coordinates of the detail texture, included after surface_base_vs.slang. See detail_texture_ps.slang.

uniform float g_flDetailTexCoordRotation = 0.0;
uniform vec2 g_vDetailTexCoordOffset = vec2(0.0);
uniform vec2 g_vDetailTexCoordScale = vec2(1.0);
out vec2 vDetailTexCoords;

#if (F_SECONDARY_UV == 1)
    uniform bool g_bUseSecondaryUvForDetailTexture = true;
#endif

// The second UV set replaces the primary coordinates when the material or the caller asks for it
void WriteDetailTexCoordsFrom(vec2 primaryCoords, bool forceSecondary)
{
    vec2 detailCoords = primaryCoords;

#if (F_SECONDARY_UV == 1)
    if (g_bUseSecondaryUvForDetailTexture || forceSecondary)
    {
        detailCoords = vTexCoord2;
    }
#endif

    vDetailTexCoords = RotateVector2D(detailCoords, g_flDetailTexCoordRotation, g_vDetailTexCoordScale.xy, g_vDetailTexCoordOffset.xy);
}

// Call it after vTexCoordOut and vTexCoord2 are written
void WriteDetailTexCoords()
{
    WriteDetailTexCoordsFrom(vTexCoordOut, false);
}

// For shaders whose detail texture starts from the mesh texcoords, not from the transformed color ones
void WriteDetailTexCoordsFromMesh(bool forceSecondary)
{
    WriteDetailTexCoordsFrom(vTEXCOORD.xy, forceSecondary);
}

#endif
