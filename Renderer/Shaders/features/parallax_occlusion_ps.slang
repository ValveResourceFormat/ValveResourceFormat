#version 460

#ifndef VRF_PARALLAX_OCCLUSION_PS_INCLUDE
#define VRF_PARALLAX_OCCLUSION_PS_INCLUDE

// Marches the height map along the view ray and returns the texture coordinates where it is hit.
// The shader declares g_tColor, whose size picks the mip level, before including this.

uniform sampler2D g_tHeight;
uniform int g_nMinSamples = 8;
uniform int g_nMaxSamples = 32;
uniform int g_nLODThreshold = 4;

in vec2 vParallaxOffsetOut;

vec2 ApplyParallaxOcclusion(vec2 texCoord, vec3 vertexNormal)
{
    vec2 texCoordDdx = dFdx(texCoord);
    vec2 texCoordDdy = dFdy(texCoord);

    // The mip level is measured on the color texture
    vec2 colorSize = vec2(textureSize(g_tColor, 0));
    vec2 texelsDdx = texCoordDdx * colorSize;
    vec2 texelsDdy = texCoordDdy * colorSize;
    vec2 texelFootprint = texelsDdx * texelsDdx + texelsDdy * texelsDdy;
    float lod = max(0.5 * log2(max(texelFootprint.x, texelFootprint.y)), 0.0);

    if (lod > float(g_nLODThreshold))
    {
        return texCoord;
    }

    // Camera forward against the outward normal is never positive on front faces, so those always march the most steps
    float facing = saturate(dot(g_vCameraDirWs, normalize(vertexNormal)));
    int stepCount = int(mix(float(g_nMaxSamples), float(g_nMinSamples), facing));
    float stepSize = 1.0 / float(stepCount);
    vec2 parallaxStep = vParallaxOffsetOut * stepSize;

    vec2 currentTexCoord = texCoord;
    float layerHeight = 1.0;
    float previousHeight = 1.0;
    float sampledHeight = 0.0;

    for (int i = 0; i < stepCount; i++)
    {
        sampledHeight = textureGrad(g_tHeight, currentTexCoord, texCoordDdx, texCoordDdy).r;

        if (sampledHeight > layerHeight)
        {
            break;
        }

        previousHeight = sampledHeight;
        currentTexCoord -= parallaxStep;
        layerHeight -= stepSize;
    }

    // Intersect the height profile between the last two samples
    float afterHit = layerHeight - sampledHeight;
    float layerBefore = layerHeight + stepSize;
    float beforeHit = layerBefore - previousHeight;
    float denominator = beforeHit - afterHit;
    float hitHeight = denominator == 0.0 ? 0.0 : (layerHeight * beforeHit - layerBefore * afterHit) / denominator;

    vec2 parallaxTexCoord = texCoord - vParallaxOffsetOut * (1.0 - hitHeight);

    // Fade the offset out over the last mip before it is skipped
    if (lod > float(g_nLODThreshold - 1))
    {
        parallaxTexCoord = mix(parallaxTexCoord, texCoord, fract(lod));
    }

    return parallaxTexCoord;
}

#endif
