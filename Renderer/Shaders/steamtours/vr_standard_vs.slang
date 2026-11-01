#version 460

// The entry file includes the features file of its game first.

#if (F_INDIRECT_TEXTURES == 2 && F_INDIRECT_TEXTURES_UV2 == 1)
    #define F_SECONDARY_UV 1
#endif

uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)

#include "../common/surface_base_vs.slang"

#if (F_VERTEX_COLOR == 1)
    in vec4 vCOLOR;
#endif

#if (F_BLEND > 0)
    in vec4 vTEXCOORD2;
    out vec2 vColorBlendValues;
    out vec4 vLayerTexCoords;

    uniform vec2 g_vLayer1Scale = vec2(1.0);
    uniform vec2 g_vLayer2Scale = vec2(1.0);
    uniform float g_flLayer1Max = 1.0;
    uniform float g_flLayer2Max = 1.0;
#endif

void main()
{
    WriteSurfaceVertex(true, 1.0, 1.0);

#if (F_VERTEX_COLOR == 1)
    vVertexColorOut *= vec4(pow(vCOLOR.rgb, vec3(2.2)), vCOLOR.a);
#endif

#if (F_BLEND > 0)
    // The layers scale the mesh coordinates, not the ones of the first layer
    vLayerTexCoords = vec4(vTEXCOORD.xy * g_vLayer1Scale, vTEXCOORD.xy * g_vLayer2Scale);

    vec2 blendPaint = saturate(vTEXCOORD2.xy);
    vColorBlendValues = blendPaint * vec2(g_flLayer1Max, g_flLayer2Max);
#endif
}
