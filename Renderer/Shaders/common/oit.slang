#version 460

#ifndef VRF_OIT_INCLUDE
#define VRF_OIT_INCLUDE

// Weighted blended order independent transparency. With this combo on, the shader draws into the
// accumulation, revealage and additive targets instead of the scene, and a composite pass blends
// the result over the scene afterwards.
#define D_OIT 0

// Includes are only expanded once, so this cannot sit inside the combo check
#include "scene_depth.slang"

#if (D_OIT == 1)

layout(location = 1) out float outputOitRevealage;
layout(location = 2) out vec3 outputOitAdditive;

// Larger for fragments closer to the camera, so that the weighted average leans toward what is in
// front. Falls off with the distance, and with the depth relative to the opaque surface behind the
// fragment, which separates layers stacked in front of a nearby wall where the distance alone
// barely changes. The weights live in a half float accumulator, so their range is bounded.
float OitWeight(vec3 contribution, float alpha, vec3 positionWs)
{
    float viewDepth = max(dot(g_vCameraDirWs, positionWs - g_vCameraPositionWs), 0.0);
    float opaqueDepth = GetSceneViewDepth(ivec2(gl_FragCoord.xy));

    float distanceTerm = clamp(0.03 / (1e-5 + pow(viewDepth / 2000.0, 4.0)), 1e-2, 3e3);

    float relativeDepth = clamp(viewDepth / max(opaqueDepth, 1e-3), 0.0, 1.0);
    float slabTerm = 1.0 - 0.9 * relativeDepth;
    slabTerm *= slabTerm * slabTerm;

    // Bright fragments count for more, capped so that HDR content cannot drown the rest
    float colorTerm = max(min(1.0, max(max(contribution.r, contribution.g), contribution.b)), alpha);

    // The accumulation already scales by alpha, scaling by it again keeps a mostly opaque surface
    // visible behind faint glass. Equal alphas, such as a stack of particles, are unaffected.
    return clamp(alpha * colorTerm * distanceTerm * slabTerm, 1e-3, 3e3);
}

// Accumulation output for a premultiplied color. The revealage target keeps the product of
// (1 - alpha) over every fragment.
vec4 OitOutput(vec3 contribution, float alpha, vec3 positionWs)
{
    float weight = OitWeight(contribution, alpha, positionWs);

    outputOitRevealage = alpha;
    outputOitAdditive = vec3(0.0);

    return vec4(contribution * weight, alpha * weight);
}

// Additive light does not cover anything and must not be normalized with the covering fragments,
// so it has its own target.
vec4 OitOutputAdditive(vec3 light, vec3 positionWs)
{
    outputOitRevealage = 0.0;
    outputOitAdditive = light;

    return vec4(0.0);
}

// For a material shader's final straight alpha color, with the blend its features choose.
vec4 OitFinish(vec4 color, vec3 positionWs)
{
#if (F_ADDITIVE_BLEND == 1) || (F_BLEND_MODE == 4)
    return OitOutputAdditive(color.rgb * color.a, positionWs);
#else
    return OitOutput(color.rgb * color.a, color.a, positionWs);
#endif
}

#endif

#endif
