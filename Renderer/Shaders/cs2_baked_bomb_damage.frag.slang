#version 460

#define MIN_ALPHA 0.4
#define FADE_OUT_DISTANCE 2048
#define PULSE_STRENGTH 3.0

#define DITHER_BLOCK_SIZE 4
#define DITHER_THRESHOLD 2

#define UV_SCALE 2.0

uniform sampler2D tColor;

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
    return mod(screenPos.x, DITHER_BLOCK_SIZE) < DITHER_THRESHOLD != mod(screenPos.y, DITHER_BLOCK_SIZE) < DITHER_THRESHOLD;
}

void main(void)
{
    if (dither())
    {
        discard;
    }
    
    float distanceToCamera = length(vtxWorldPos - g_vCameraPositionWs);

    float unitPhase = mod(g_flTime - vtxPhase / PULSE_STRENGTH, 1.0);

    float alpha = max(0.0, 1.0 - distanceToCamera / FADE_OUT_DISTANCE);
    alpha *= 1.0 - (unitPhase * unitPhase);
    alpha = mix(MIN_ALPHA, 1.0, alpha);

    vec2 uv = vtxUv * UV_SCALE + vec2(-unitPhase, 0.0);
    outputColor = texture(tColor, uv) * vtxColor;
    if (outputColor.a <= 0.1)
    {
        discard;
    }

    outputColor.a *= alpha;
}
