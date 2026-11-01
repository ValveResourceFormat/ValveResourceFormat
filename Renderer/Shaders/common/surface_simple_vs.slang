#version 460

#ifndef VRF_SURFACE_SIMPLE_VS_INCLUDE
#define VRF_SURFACE_SIMPLE_VS_INCLUDE

// Vertex shader of a lit surface that is drawn from its mesh as is, with the vertex paint decoded from sRGB
// when its features file declares F_VERTEX_COLOR or F_PAINT_VERTEX_COLORS.
// The shader includes its features file and declares g_vColorTint first, see surface_base_vs.slang.

#include "surface_base_vs.slang"
#include "surface_model_tint_vs.slang"

#if (F_VERTEX_COLOR == 1) || (F_PAINT_VERTEX_COLORS == 1)
    in vec4 vCOLOR;
#endif

void main()
{
    WriteSurfaceVertex(true);

#if (F_VERTEX_COLOR == 1) || (F_PAINT_VERTEX_COLORS == 1)
    vVertexColorOut *= SrgbGammaToLinear(vCOLOR);
#endif
}

#endif
