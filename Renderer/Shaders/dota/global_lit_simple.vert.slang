#version 460

#include "../common/utils.slang"
#include "../common/instancing.slang"
#include "../common/animation.slang"

//Parameter defines - These are default values and can be overwritten based on material/model parameters
#define F_PAINT_VERTEX_COLORS 0
//End of parameter defines

in vec3 vPOSITION;
in vec2 vTEXCOORD;
#include "../common/compression.slang"

#if (F_PAINT_VERTEX_COLORS == 1)
    in vec4 vCOLOR;
    out vec4 vVertexColorOut;
#endif

out vec3 vFragPosition;
out vec3 vNormalOut;
out vec4 vTangentOut;
out vec2 vTexCoordOut;
flat out vec4 vTintColorFadeOut;

uniform vec2 g_vTexCoordOffset = vec2(0.0);
uniform vec2 g_vTexCoordScale = vec2(1.0);

#include "../common/view_constants.slang"
#include "../common/skinned_vertex.slang"

void main()
{
    WriteSkinnedPositionAndTangentFrame();

    vec4 tint = GetObjectTintSrgb();
    vTintColorFadeOut = vec4(SrgbGammaToLinear(tint.rgb), tint.a);
    vTexCoordOut = vTEXCOORD * g_vTexCoordScale.xy + g_vTexCoordOffset.xy;

    #if (F_PAINT_VERTEX_COLORS == 1)
        vVertexColorOut = vCOLOR;
    #endif
}
