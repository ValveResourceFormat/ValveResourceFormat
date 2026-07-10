#version 460

#define FADE_OUT_DISTANCE 2048

uniform sampler2D tColor;
uniform float time;

flat in vec4 vtxColor;
in vec2 vtxUv;
in vec3 vtxPos;
in vec4 vtxClipPos;
in vec3 vtxWorldPos;
flat in float vtxPhase;

out vec4 outputColor;

#include "common/utils.slang"
#include "common/ViewConstants.slang"

bool dither()
{
    vec2 screenPos = (vtxClipPos.xy / vtxClipPos.w * 0.5 + 0.5) * g_vViewportSize;
    return mod(screenPos.x, 4) < 2 != mod(screenPos.y, 4) < 2;
}

void main(void)
{
    if (dither())
    {
        discard;
    }
    
    float distanceToCamera = length(vtxWorldPos - g_vCameraPositionWs);
    if (distanceToCamera > FADE_OUT_DISTANCE)
    {
        discard;
    }
    float alpha = 1.0 - distanceToCamera / FADE_OUT_DISTANCE;

    vec2 uv = vtxUv + vec2(-mod(time - vtxPhase, 1.0) * 2.0 + 1.0, 0.0);
    outputColor = texture(tColor, uv) * vtxColor;
    if (outputColor.a <= 0.1)
    {
        discard;
    }

    outputColor.a *= alpha;
}
