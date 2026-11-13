#version 460

#ifndef VRF_SURFACE_PAINTED_VS_INCLUDE
#define VRF_SURFACE_PAINTED_VS_INCLUDE

// Vertex shader of a lit surface drawn from the mesh as is, with the vertex paint used as stored
// when the features file of the shader declares F_VERTEX_COLOR or F_PAINT_VERTEX_COLORS.
// The shader includes that features file and declares g_vColorTint first, see surface_base_vs.slang.

#include "../common/surface_base_vs.slang"
#include "../common/surface_model_tint_vs.slang"

#if (F_VERTEX_COLOR == 1) || (F_PAINT_VERTEX_COLORS == 1)
    in vec4 vCOLOR;
#endif

void main()
{
    WriteSurfaceVertex(true);

#if (F_VERTEX_COLOR == 1) || (F_PAINT_VERTEX_COLORS == 1)
    vVertexColorOut *= GetUndecodedVertexPaint(vCOLOR);
#endif
}

#endif
