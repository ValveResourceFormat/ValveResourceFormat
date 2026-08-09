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

// m_bBlendFramesSeq0. Gated on a uniform rather than on vFrameBlend so that the second fetch sits in
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
in float vFrameBlend;
in vec4 vColor;
in vec3 vFragPosition;

out vec4 fragColor;

// Shifts the hue and scales saturation and value, going through HSV and back. Kept identical to the
// copy in particle_sprite.frag.slang, which is ported from the spritecard pixel shader.
vec3 ApplyHsvShift(vec3 rgb)
{
    vec4 k = vec4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    vec4 p = mix(vec4(rgb.bg, k.wz), vec4(rgb.gb, k.xy), step(rgb.b, rgb.g));
    vec4 q = mix(vec4(p.xyw, rgb.r), vec4(rgb.r, p.yzx), step(p.x, rgb.r));

    float d = q.x - min(q.w, q.y);
    float e = 1.0e-10;
    vec3 hsv = vec3(abs(q.z + (q.w - q.y) / ((6.0 * d) + e)), d / (q.x + e), q.x);

    hsv.x = fract(hsv.x + uHsvShift.x);
    hsv.y = saturate(hsv.y * uHsvShift.y);
    hsv.z *= uHsvShift.z;

    vec4 k2 = vec4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    vec3 p2 = abs((fract(hsv.xxx + k2.xyz) * 6.0) - k2.www);

    return hsv.z * mix(k2.xxx, saturate(p2 - k2.xxx), hsv.y);
}

void main(void) {
    vec4 color = texture(uTexture, vTexCoordOut);

    if (uBlendFrames)
    {
        vec4 nextFrame = texture(uTexture, vTexCoordNextFrameOut);

        if (uMaxLuminanceFrameBlend)
        {
            float weight = (1.0 - vFrameBlend) * sqrt(max(0.001, GetLuma(color.rgb)));
            float nextWeight = vFrameBlend * sqrt(max(0.001, GetLuma(nextFrame.rgb)));
            color = ((color * weight) + (nextFrame * nextWeight)) / (weight + nextWeight);
        }
        else
        {
            color = mix(color, nextFrame, vFrameBlend);
        }
    }

    vec3 finalColor = vColor.rgb * color.rgb;

    float alpha = color.a;

    if (uAlphaRemapRange.x < uAlphaRemapRange.y)
    {
        alpha = smoothstep(uAlphaRemapRange.x, uAlphaRemapRange.y, alpha);
    }

    finalColor = mix(finalColor, vec3(GetLuma(finalColor)), uDesaturation);

    if (uHsvShift != vec3(0.0, 1.0, 1.0))
    {
        finalColor = ApplyHsvShift(finalColor);
    }

    finalColor = finalColor * (uOverbrightFactor * uColorFactor);

    if (uSaturateColorPreAlphaBlend)
    {
        finalColor = saturate(finalColor);
    }

    alpha *= vColor.a;

    // MOD2X composites like ADD -- see the note in particle_sprite.frag.slang.
    bool isAdditive = uBlendMode == PARTICLE_OUTPUT_BLEND_MODE_ADD
        || uBlendMode == PARTICLE_OUTPUT_BLEND_MODE_MOD2X;

    // Additive contributions fade with fog density instead of tinting toward the fog color.
    if (isAdditive)
    {
        finalColor *= GetFogAttenuation(vFragPosition);
    }
    else
    {
        ApplyFog(finalColor, vFragPosition);
    }

    // Premultiplied alpha; additive keeps the color term and zeroes the blend weight.
    fragColor = vec4(finalColor * alpha, isAdditive ? 0.0 : alpha);

    if (g_iRenderMode == renderMode_Color)
    {
        fragColor = vec4(finalColor, 1.0);
    }
}
