#version 460

#include "common/utils.slang"
#include "common/ViewConstants.slang"
#include "common/fog.slang"

// Render modes -- Switched on/off by code
#define renderMode_Color 0

// ParticleBlendMode_t, mirroring Renderer/ParticleRenderer/Enums/ParticleBlendMode.cs. See the same
// uniform in particle_sprite.frag.slang: a runtime branch keeps this at one compiled variant.
#define PARTICLE_OUTPUT_BLEND_MODE_ADD   1
#define PARTICLE_OUTPUT_BLEND_MODE_MOD2X 5
uniform int uBlendMode = 0; // PARTICLE_OUTPUT_BLEND_MODE_ALPHA

uniform sampler2D uTexture;

// m_bBlendFramesSeq0. Gated on a uniform rather than on vFrameBlendOut so that the second fetch sits in
// draw-uniform control flow.
uniform bool uBlendFrames = true;
uniform bool uMaxLuminanceFrameBlend = false;

// The shared CBaseRendererSource2 colour treatment, the same set the sprite renderer uploads.
uniform float uOverbrightFactor;
uniform float uColorFactor = 1.0; // m_flDiffuseAmount + m_flSelfIllumAmount
uniform float uDesaturation = 0.0; // m_flDesaturation
uniform bool uSaturateColorPreAlphaBlend = true;
uniform vec3 uHsvShift = vec3(0.0, 1.0, 1.0);
uniform vec2 uAlphaRemapRange = vec2(1.0, 0.0);

in vec2 vTexCoordOut;
in vec2 vTexCoordNextFrameOut;
in float vFrameBlendOut;
in vec4 vColor;
in vec3 vFragPosition;

out vec4 fragColor;

// Shifts the hue and scales saturation and value, going through HSV and back. The same conversion the
// spritecard pixel shader rolls by hand, including the normalize and rescale that preserves overbright.
vec3 ApplyHsvShift(vec3 sourceColor)
{
    float overbright = max(max(sourceColor.r, max(sourceColor.g, sourceColor.b)), 1.0);
    vec3 rgb = saturate(sourceColor / overbright);

    float valueMax = max(rgb.r, max(rgb.g, rgb.b));
    float chroma = valueMax - min(rgb.r, min(rgb.g, rgb.b));

    vec3 hsv = vec3(0.0, 0.0, valueMax);

    if (chroma != 0.0)
    {
        vec3 distance = (vec3(valueMax) - rgb) / vec3(chroma);
        vec3 delta = distance - distance.zxy;

        if (rgb.r >= valueMax)      { hsv = vec3(delta.z,       chroma / valueMax, valueMax); }
        else if (rgb.g >= valueMax) { hsv = vec3(delta.x + 2.0, chroma / valueMax, valueMax); }
        else                        { hsv = vec3(delta.y + 4.0, chroma / valueMax, valueMax); }

        hsv.x = fract(hsv.x * (1.0 / 6.0));
    }

    float hue = fract(hsv.x + uHsvShift.x);
    vec2 satVal = saturate(hsv.yz * uHsvShift.yz);

    if (satVal.x == 0.0)
    {
        return vec3(satVal.y) * overbright;
    }

    float sector = hue * 6.0;
    float index = floor(sector);
    float f = sector - index;

    float p = satVal.y * (1.0 - satVal.x);
    float q = satVal.y * (1.0 - (satVal.x * f));
    float t = satVal.y * (1.0 - (satVal.x * (1.0 - f)));

    if (index == 0.0) { return vec3(satVal.y, t, p) * overbright; }
    if (index == 1.0) { return vec3(q, satVal.y, p) * overbright; }
    if (index == 2.0) { return vec3(p, satVal.y, t) * overbright; }
    if (index == 3.0) { return vec3(p, q, satVal.y) * overbright; }
    if (index == 4.0) { return vec3(t, p, satVal.y) * overbright; }

    return vec3(satVal.y, p, q) * overbright;
}

void main(void) {
    vec4 color = texture(uTexture, vTexCoordOut);

    if (uBlendFrames)
    {
        vec4 nextFrame = texture(uTexture, vTexCoordNextFrameOut);

        if (uMaxLuminanceFrameBlend)
        {
            float weight = (1.0 - vFrameBlendOut) * sqrt(max(0.001, GetLuma(color.rgb)));
            float nextWeight = vFrameBlendOut * sqrt(max(0.001, GetLuma(nextFrame.rgb)));
            color = ((color * weight) + (nextFrame * nextWeight)) / (weight + nextWeight);
        }
        else
        {
            color = mix(color, nextFrame, vFrameBlendOut);
        }
    }

    vec3 finalColor = vColor.rgb * color.rgb;

    if (uHsvShift != vec3(0.0, 1.0, 1.0))
    {
        finalColor = ApplyHsvShift(finalColor);
    }

    float alpha = color.a;

    if (uAlphaRemapRange.x < uAlphaRemapRange.y)
    {
        alpha = smoothstep(uAlphaRemapRange.x, uAlphaRemapRange.y, alpha);
    }

    finalColor = mix(finalColor, vec3(GetLuma(finalColor)), uDesaturation);

    alpha *= vColor.a;

    if (uBlendMode == PARTICLE_OUTPUT_BLEND_MODE_MOD2X)
    {
        // Its own output path, neutral at 0.5 -- see the note in particle_sprite.frag.slang.
        vec3 mod2xColor = mix(vec3(0.5), mix(vec3(0.5), finalColor, vColor.rgb), vec3(alpha));

        fragColor = vec4(saturate(mod2xColor), alpha);
    }
    else
    {
        finalColor = finalColor * (uOverbrightFactor * uColorFactor);

        if (uSaturateColorPreAlphaBlend)
        {
            finalColor = saturate(finalColor);
        }

        bool isAdditive = uBlendMode == PARTICLE_OUTPUT_BLEND_MODE_ADD;

        // Additive contributions fade with fog density instead of tinting toward the fog color.
        if (isAdditive)
        {
            finalColor *= GetFogAttenuation(vFragPosition);
        }
        else
        {
            ApplyFog(finalColor, vFragPosition);
        }

        fragColor = vec4(finalColor * alpha, isAdditive ? 0.0 : alpha);
    }

    if (g_iRenderMode == renderMode_Color)
    {
        fragColor = vec4(finalColor, 1.0);
    }
}
