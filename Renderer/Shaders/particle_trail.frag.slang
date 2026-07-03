#version 460

#include "common/utils.slang"
#include "common/ViewConstants.slang"
#include "common/fog.slang"

// Render modes -- Switched on/off by code
#define renderMode_Color 0
#define F_ADDITIVE_BLEND 0
#define F_MOD2X 0

uniform vec3 uColor;
uniform sampler2D uTexture;

in vec2 uv;
in vec3 vFragPosition;

uniform vec2 uUvOffset;
uniform vec2 uUvScale;

uniform float uAlpha;
uniform float uOverbrightFactor;

out vec4 fragColor;

void main(void) {
    vec4 color = texture(uTexture, uUvOffset + uv * uUvScale);

    vec3 finalColor = uColor * color.rgb;
    float blendingFactor = uOverbrightFactor * GetLuma(finalColor.rgb);

#if (F_ADDITIVE_BLEND == 1) || (F_MOD2X == 1)
    // Additive and mod2x contributions fade with fog density instead of tinting toward the fog color.
    #if F_MOD2X
    finalColor = mix(vec3(0.5), finalColor, GetFogAttenuation(vFragPosition)); // fades toward the blend-neutral 0.5
    #else
    finalColor *= GetFogAttenuation(vFragPosition);
    #endif
#else
    ApplyFog(finalColor, vFragPosition);
#endif

    fragColor = vec4(finalColor, uAlpha * color.a * blendingFactor);

    if (g_iRenderMode == renderMode_Color)
    {
        fragColor = vec4(finalColor, 1.0);
    }
}
