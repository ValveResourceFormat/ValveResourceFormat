#version 460

//? #include "ViewConstants.glsl"

uniform sampler2DShadow g_tShadowDepthBufferDepth;

float CalculateSunShadowMapVisibility(vec3 vPosition)
{
    vec4 projCoords = g_matWorldToShadow * vec4(vPosition, 1.0);
    projCoords.xyz /= projCoords.w;

    vec2 shadowCoords = clamp(projCoords.xy * 0.5 + 0.5, vec2(-1), vec2(2));

    // Note: Bias is added of clamp, so that the value is never zero (or negative)
    // as the comparison with <= 0 values produces shadow
    float currentDepth = saturate(projCoords.z) + g_flSunShadowBias;

    // To skip PCF
    // return 1 - textureLod(g_tShadowDepthBufferDepth, vec3(shadowCoords, currentDepth), 0).r;

    float shadow = 0.0;
    vec2 texelSize = 1.0 / textureSize(g_tShadowDepthBufferDepth, 0);
    for(int x = -1; x <= 1; ++x)
    {
        for(int y = -1; y <= 1; ++y)
        {
            float pcfDepth = textureLod(g_tShadowDepthBufferDepth, vec3(shadowCoords + vec2(x, y) * texelSize, currentDepth), 0).r;
            shadow += pcfDepth;
        }
    }

    shadow /= 9.0;

    vec2 distFromEdge = min(vec2(1.0) - abs(projCoords.xy), vec2(1.0));
    float edgeDistance = min(distFromEdge.x, distFromEdge.y);
    float edgeFadeOut = smoothstep(0.0, 0.08, edgeDistance);
    shadow *= edgeFadeOut;
    
    return 1.0 - shadow;
}
