#version 460

#define F_ALPHA_TEST 0 // most shaders

#include "common/texturing.slang"
#include "common/lighting.particle.slang"

in vec2 vTexCoordOut;
in vec4 vTintOut;
in vec3 vPositionWs;

uniform sampler2D g_tColor; // SrgbRead(true)

out vec4 outputColor;

void main()
{
    vec4 color = texture(g_tColor, vTexCoordOut);

#if (F_ALPHA_TEST == 1) || (F_ALPHA_TEST == 2)
    color.a = AlphaTestAntiAliasing(color.a, vTexCoordOut);
#endif

    outputColor = color * vTintOut;

    ParticleLight_t light = CalculateParticleLighting(vPositionWs, normalize(g_vCameraPositionWs - vPositionWs), 0.5);

    outputColor.rgb *= light.Ambient + light.Direct + 0.2;
}
