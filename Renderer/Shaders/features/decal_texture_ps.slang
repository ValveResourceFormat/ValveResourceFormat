#version 460

#ifndef VRF_DECAL_TEXTURE_PS_INCLUDE
#define VRF_DECAL_TEXTURE_PS_INCLUDE

// The decal texture of materials with F_DECAL_TEXTURE, laid over the albedo.

uniform int F_DECAL_BLEND_MODE;

uniform sampler2D g_tDecal; // SrgbRead(true)

// Blends the decal over the albedo, or multiplies the albedo by it when F_DECAL_BLEND_MODE is not 0
vec3 ApplyDecalTexture(vec3 albedo, vec2 coords)
{
    vec4 decalTex = texture(g_tDecal, coords);

    return (F_DECAL_BLEND_MODE == 0)
        ? mix(albedo, decalTex.rgb, decalTex.a)
        : albedo * decalTex.rgb;
}

#endif
