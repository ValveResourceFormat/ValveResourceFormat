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

// SpriteCardTextureChannel_t: how the sampled texel maps into colour/alpha. Defaults to MIX_RGBA
// (colour from RGB, alpha from A). Colour-only modes take colour from a channel (or RGB) and drop the
// texture alpha; coverage-only modes take colour from the particle and pull alpha from one channel. The
// dual-texture MASK / *_RGBALPHA modes need a second texture the renderer does not bind, so they fall
// through to straight RGBA.
#define SPRITECARD_TEXTURE_CHANNEL_MIX_RGB    0
#define SPRITECARD_TEXTURE_CHANNEL_MIX_A      2
#define SPRITECARD_TEXTURE_CHANNEL_MIX_R      9
#define SPRITECARD_TEXTURE_CHANNEL_MIX_G      10
#define SPRITECARD_TEXTURE_CHANNEL_MIX_B      11
#define SPRITECARD_TEXTURE_CHANNEL_MIX_RALPHA 12
#define SPRITECARD_TEXTURE_CHANNEL_MIX_GALPHA 13
#define SPRITECARD_TEXTURE_CHANNEL_MIX_BALPHA 14
uniform int uTextureChannels = 1; // MIX_RGBA

in vec2 vTexCoordOut;
in vec4 vColor;
in vec3 vFragPosition;

out vec4 fragColor;

void main(void) {
    vec4 color = texture(uTexture, vTexCoordOut);

    switch (uTextureChannels)
    {
        // Colour-only: colour from a channel (or RGB), texture alpha dropped (opaque texel).
        case SPRITECARD_TEXTURE_CHANNEL_MIX_RGB: color = vec4(color.rgb, 1.0); break;
        case SPRITECARD_TEXTURE_CHANNEL_MIX_R:   color = vec4(color.rrr, 1.0); break;
        case SPRITECARD_TEXTURE_CHANNEL_MIX_G:   color = vec4(color.ggg, 1.0); break;
        case SPRITECARD_TEXTURE_CHANNEL_MIX_B:   color = vec4(color.bbb, 1.0); break;
        // Coverage-only: colour from the particle, a single channel supplies alpha.
        case SPRITECARD_TEXTURE_CHANNEL_MIX_A:      color = vec4(1.0, 1.0, 1.0, color.a); break;
        case SPRITECARD_TEXTURE_CHANNEL_MIX_RALPHA: color = vec4(1.0, 1.0, 1.0, color.r); break;
        case SPRITECARD_TEXTURE_CHANNEL_MIX_GALPHA: color = vec4(1.0, 1.0, 1.0, color.g); break;
        case SPRITECARD_TEXTURE_CHANNEL_MIX_BALPHA: color = vec4(1.0, 1.0, 1.0, color.b); break;
        default: break; // MIX_RGBA and the unsupported dual-texture modes use the texel as-is
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
