#version 460

#include "common/utils.slang"
#include "common/ViewConstants.slang"
#include "common/fog.slang"

// Render modes -- Switched on/off by code
#define renderMode_Color 0
#define F_MOD2X 0 // global_lit_underlord_fx, spritecard, tower_range, tower_range_halo
#define F_ADDITIVE_BLEND 0

uniform sampler2D uTexture;
uniform float uOverbrightFactor;
uniform float uColorFactor = 1.0; // m_flDiffuseAmount + m_flSelfIllumAmount
uniform vec2 uAlphaRemapScaleBias = vec2(1.0, 0.0);
// m_nTextureChannels: 0 = RGBA as-is, 1 = MIX_RALPHA (red channel is the alpha, color from the particle)
uniform int uTextureChannels = 0;

in vec2 vTexCoordOut;
in vec4 vColor;
in vec3 vFragPosition;

out vec4 fragColor;

void main(void) {
    vec4 color = texture(uTexture, vTexCoordOut);

    if (uTextureChannels == 1)
    {
        color = vec4(1.0, 1.0, 1.0, color.r);
    }

    // spritecard shading: brightness from diffuse+selfillum, saturated before the alpha blend.
    vec3 finalColor = vColor.rgb * color.rgb;
    finalColor = saturate(finalColor * (uOverbrightFactor * uColorFactor));

    // Texture alpha remap, then the per-particle alpha.
    float alpha = saturate(color.a * uAlphaRemapScaleBias.x + uAlphaRemapScaleBias.y);
    alpha *= vColor.a;

#if (F_ADDITIVE_BLEND == 1) || (F_MOD2X == 1)
    // Additive and mod2x contributions fade with fog density instead of tinting toward the fog color.
    #if F_MOD2X
    alpha *= GetFogAttenuation(vFragPosition); // fades the output toward the blend-neutral 0.5
    #else
    finalColor *= GetFogAttenuation(vFragPosition);
    #endif
#else
    ApplyFog(finalColor, vFragPosition);
#endif

#if F_MOD2X
    fragColor = vec4(mix(vec3(0.5), finalColor, vec3(alpha)), alpha);
#else
    // Premultiplied alpha; additive keeps the color term and zeroes the blend weight.
    float blendWeight = (F_ADDITIVE_BLEND == 1) ? 0.0 : 1.0;
    fragColor = vec4(finalColor * alpha, alpha * blendWeight);
#endif

    if (g_iRenderMode == renderMode_Color)
    {
        fragColor = vec4(finalColor, 1.0);
    }
}
