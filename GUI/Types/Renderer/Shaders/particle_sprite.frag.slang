#version 460

#include "common/utils.slang"
#include "common/ViewConstants.slang"

// Render modes -- Switched on/off by code
#define renderMode_Color 0
#define F_MOD2X 0

uniform sampler2D uTexture;
uniform float uOverbrightFactor;

in vec2 vTexCoordOut;
in vec4 vColor;

out vec4 fragColor;

void main(void) {
    vec4 color = texture(uTexture, vTexCoordOut);

    vec3 finalColor = vColor.rgb * color.rgb;
    finalColor *= uOverbrightFactor;

    fragColor = vec4(finalColor, vColor.a * color.a);

#if F_MOD2X
    fragColor = vec4(mix(vec3(0.5), fragColor.rgb, vec3(fragColor.a)), fragColor.a);
#endif

    if (g_iRenderMode == renderMode_Color)
    {
        fragColor = vec4(finalColor, 1.0);
    }
}
