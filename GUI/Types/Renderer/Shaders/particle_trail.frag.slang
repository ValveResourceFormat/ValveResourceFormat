#version 460

#include "common/utils.slang"
#include "common/ViewConstants.slang"

// Render modes -- Switched on/off by code
#define renderMode_Color 0

uniform vec3 uColor;
uniform sampler2D uTexture;

in vec2 uv;

uniform vec2 uUvOffset;
uniform vec2 uUvScale;

uniform float uAlpha;
uniform float uOverbrightFactor;

out vec4 fragColor;

void main(void) {
    vec4 color = texture(uTexture, uUvOffset + uv * uUvScale);

    vec3 finalColor = uColor * color.rgb;
    float blendingFactor = uOverbrightFactor * GetLuma(finalColor.rgb);

    fragColor = vec4(finalColor, uAlpha * color.a * blendingFactor);

    if (g_iRenderMode == renderMode_Color)
    {
        fragColor = vec4(finalColor, 1.0);
    }
}
