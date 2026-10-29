#version 460

#include "environment_parallax_layered_features.slang"

uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)

#include "../common/surface_base_vs.slang"
#include "../common/surface_model_tint_vs.slang"

in vec4 vCOLOR;

void main()
{
    WriteSurfaceVertex(true);

    // The paint fades towards white by its alpha
    vVertexColorOut.rgb *= mix(vec3(1.0), SrgbGammaToLinear(vCOLOR.rgb), vCOLOR.a);
}
