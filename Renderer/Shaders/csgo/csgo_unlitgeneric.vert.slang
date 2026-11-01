#version 460

#include "csgo_unlitgeneric_features.slang"

uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)

#include "../common/surface_base_vs.slang"
#include "../common/surface_model_tint_vs.slang"

#if (F_VERTEX_COLOR == 1)
    in vec4 vCOLOR;
#endif

#if (F_TWOTEXTURE == 1)
    uniform vec2 g_vTex2CoordCenter = vec2(0.5);
    uniform vec2 g_vTex2CoordScale = vec2(1.0);
    uniform float g_flTex2CoordRotation = 0.0;
    uniform vec2 g_vTex2CoordOffset = vec2(0.0);

    out vec2 vTexCoord2Out;
#endif

void main()
{
    WriteSurfaceVertex(true);

#if (F_VERTEX_COLOR == 1)
    vVertexColorOut *= GetUndecodedVertexPaint(vCOLOR);
#endif

#if (F_TWOTEXTURE == 1)
    // The second texture has its own transform of the mesh texcoords, and does not scroll or animate
    vTexCoord2Out = TransformSecondaryTexCoord(vTEXCOORD.xy, g_flTex2CoordRotation, g_vTex2CoordScale, g_vTex2CoordOffset, g_vTex2CoordCenter);
#endif
}
