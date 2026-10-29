#version 460

#ifndef VRF_PARALLAX_OCCLUSION_VS_INCLUDE
#define VRF_PARALLAX_OCCLUSION_VS_INCLUDE

// Texture space offset of the full height march, included after surface_base_vs.slang.

uniform float g_flHeightMapScale = 0.02;

out vec2 vParallaxOffsetOut;

vec2 GetParallaxOcclusionOffset(vec3 positionWs, vec3 normalWs, vec4 tangentWs)
{
    vec3 tangent = normalize(tangentWs.xyz - normalWs * dot(tangentWs.xyz, normalWs));
    vec3 bitangent = GetBitangent(normalWs, vec4(tangent, tangentWs.w));

    vec3 positionToCamera = g_vCameraPositionWs - positionWs;
    vec3 viewTs = vec3(dot(positionToCamera, tangent), dot(positionToCamera, bitangent), dot(positionToCamera, normalWs));

    return normalize(viewTs).xy * (length(viewTs.xy) / viewTs.z) * g_flHeightMapScale;
}

#endif
