#version 460

#include "common/utils.slang"

uniform int g_nNumSamplesMSAA = 1;
uniform bool g_bFlipY = false;

layout (binding = 0) uniform sampler2DMS g_tColorBuffer;
layout (location = 0) out vec4 outputColor;

float InvNumSamples = 1.0 / float(g_nNumSamplesMSAA);

vec4 SampleColorBuffer(vec2 coords)
{
    vec4 vColorMSAA = vec4(0.0);

    ivec2 pixelCoords = ivec2(coords.xy);
    pixelCoords.y = g_bFlipY ? textureSize(g_tColorBuffer).y - pixelCoords.y - 1 : pixelCoords.y;

    for (int i = 0; i < g_nNumSamplesMSAA; i++)
    {
        vec4 sampleColor = texelFetch(g_tColorBuffer, pixelCoords, i);
        sampleColor = clamp(sampleColor, vec4(0), vec4(65504));

        vColorMSAA += sampleColor.rgba * InvNumSamples;
    }

    return vColorMSAA;
}

void main()
{
    outputColor = SampleColorBuffer(gl_FragCoord.xy);
}
