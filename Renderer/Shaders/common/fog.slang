#version 460

#ifndef VRF_FOG_INCLUDE
#define VRF_FOG_INCLUDE

#include "view_constants.slang"
#include "shared_combos.slang"
#include "utils.slang"

// Fog pixel shaders.

// Per-material
uniform bool g_bFogEnabled = true;

// A shader that sets this fogs with a texture of the fog color and opacity, over the horizontal distance from the
// camera and the height, instead of the fog color, exponents and maximum opacity.
#ifndef FOG_GRADIENT_TEXTURE
#define FOG_GRADIENT_TEXTURE 0
#endif

// A shader that sets this to 1 looks the texture up by the distance from the camera in all three axes alone, at the
// same coordinate in both directions of the texture.
#ifndef FOG_GRADIENT_TEXTURE_DISTANCE
#define FOG_GRADIENT_TEXTURE_DISTANCE 0
#endif

#if (FOG_GRADIENT_TEXTURE)
uniform sampler2D g_tGradientFogTexture; // SrgbRead(true)
#endif

// The fog color in rgb and its opacity in a
vec4 GetGradientFog(vec3 positionWS, vec3 cameraCenteredPixelPos)
{
    #if (FOG_GRADIENT_TEXTURE && FOG_GRADIENT_TEXTURE_DISTANCE)
        float fogDistance = length(cameraCenteredPixelPos) * g_vGradientFogBiasAndScale.z + g_vGradientFogBiasAndScale.x;
        return texture(g_tGradientFogTexture, vec2(fogDistance));
    #elif (FOG_GRADIENT_TEXTURE)
        vec2 fogTexCoord = vec2(length(cameraCenteredPixelPos.xy), positionWS.z) * g_vGradientFogBiasAndScale.zw + g_vGradientFogBiasAndScale.xy;
        return texture(g_tGradientFogTexture, fogTexCoord);
    #else
        vec2 gradientFogComponents; // x for the distance from the camera, y for height in World Space

        #if (FOG_GRADIENT_HORIZONTAL_DISTANCE)
            gradientFogComponents.x = length(cameraCenteredPixelPos.xy);
        #else
            gradientFogComponents.x = length(cameraCenteredPixelPos);
        #endif

        gradientFogComponents.y = positionWS.z;

        gradientFogComponents = pow( clamp(gradientFogComponents * g_vGradientFogBiasAndScale.zw + g_vGradientFogBiasAndScale.xy, 0.0001, 1.0), m_vGradientFogExponents );

        float gradientFogBlend = gradientFogComponents.x * gradientFogComponents.y * g_vGradientFogColor_Opacity.a;

        return vec4(g_vGradientFogColor_Opacity.rgb, gradientFogBlend);
    #endif
}

float GetGradientFogDistanceSquared(vec3 cameraCenteredPixelPos)
{
    #if (FOG_GRADIENT_HORIZONTAL_DISTANCE)
        return length_squared(cameraCenteredPixelPos.xy);
    #else
        return length_squared(cameraCenteredPixelPos);
    #endif
}

// The culling height is where a height term reaches zero, and the term grows on the side of it its scale points to.
// Without a height range the scale is 0 and the term is 1 at every height.
bool IsFogHeightInRange(float height, float heightScale, float cullingHeight)
{
    if (heightScale > 0.0)
    {
        return height > cullingHeight;
    }

    if (heightScale < 0.0)
    {
        return height < cullingHeight;
    }

    return true;
}

// A vertical exponent of 0 also leaves the height term at 1
bool IsGradientFogHeightInRange(float height)
{
    if (m_vGradientFogExponents.y == 0.0)
    {
        return true;
    }

    return IsFogHeightInRange(height, g_vGradientFogBiasAndScale.w, g_vGradientFogCullingParams.y);
}

void ApplyGradientFog(inout vec3 pixelColor, vec3 positionWS, vec3 cameraCenteredPixelPos)
{
    vec4 fog = GetGradientFog(positionWS, cameraCenteredPixelPos);
    pixelColor = mix(pixelColor, fog.rgb, fog.a);
}

uniform samplerCube g_tFogCubeTexture; // reserved

// The fog color in rgb and its opacity in a
vec4 GetCubemapFog(vec3 positionWS, vec3 posRelativeToCamera)
{
#if (FOG_CUBE_BLEND_MAX)
    float distanceTerm = pow(max(length(posRelativeToCamera) * g_vCubeFog_Offset_Scale_Bias_Exponent.y + g_vCubeFog_Offset_Scale_Bias_Exponent.x, 0.0), g_vCubeFog_Offset_Scale_Bias_Exponent.w);

    // Without a height range the height term is left at zero
    float heightTerm = 0.0;

    if (g_vCubeFog_Height_Offset_Scale_Exponent_Log2Mip.z != 0.0)
    {
        heightTerm = pow(max(positionWS.z * g_vCubeFog_Height_Offset_Scale_Exponent_Log2Mip.y + g_vCubeFog_Height_Offset_Scale_Exponent_Log2Mip.x, 0.0), g_vCubeFog_Height_Offset_Scale_Exponent_Log2Mip.z);
    }

    float cubemapFogTerm = max(distanceTerm, heightTerm);

    vec3 fogCoords = normalize((vec4(normalize(posRelativeToCamera), 0.0) * g_matvCubeFogSkyWsToOs).xyz);
    float cubemapFogLod = saturate(1.0 - cubemapFogTerm * g_vCubeFog_Offset_Scale_Bias_Exponent.z) * g_vCubeFog_Height_Offset_Scale_Exponent_Log2Mip.w;

    vec3 cubemapFogColor = textureLod(g_tFogCubeTexture, fogCoords, cubemapFogLod).rgb;

    #if (FOG_CUBE_EXPOSURE_BIAS)
        cubemapFogColor *= g_vCubeFogCullingParams_ExposureBias_MaxOpacity.z;
    #endif

    return vec4(cubemapFogColor, saturate(cubemapFogTerm));
#else
    vec2 cubeFogComponents;
    cubeFogComponents.x = pow(max(length(posRelativeToCamera) * g_vCubeFog_Offset_Scale_Bias_Exponent.y + g_vCubeFog_Offset_Scale_Bias_Exponent.x, 0.0001), g_vCubeFog_Offset_Scale_Bias_Exponent.w);
    cubeFogComponents.y = pow(max(positionWS.z * g_vCubeFog_Height_Offset_Scale_Exponent_Log2Mip.y + g_vCubeFog_Height_Offset_Scale_Exponent_Log2Mip.x, 0.0001), g_vCubeFog_Height_Offset_Scale_Exponent_Log2Mip.z);

    float cubemapFogBlend = saturate(cubeFogComponents.x) * saturate(cubeFogComponents.y);

    vec3 fogCoords = normalize((vec4(normalize(posRelativeToCamera), 0.0) * g_matvCubeFogSkyWsToOs).xyz);
    float cubemapFogLod = saturate( 1.0 - cubemapFogBlend * g_vCubeFog_Offset_Scale_Bias_Exponent.z ) * g_vCubeFog_Height_Offset_Scale_Exponent_Log2Mip.w;

    float cubemapFogOpacity = saturate(cubemapFogBlend) * g_vCubeFogCullingParams_ExposureBias_MaxOpacity.a;

    vec3 cubemapFogColor = textureLod(g_tFogCubeTexture, fogCoords.xyz, cubemapFogLod).rgb;
    cubemapFogColor *= g_vCubeFogCullingParams_ExposureBias_MaxOpacity.z;

    return vec4(cubemapFogColor, cubemapFogOpacity);
#endif
}

void ApplyCubemapFog(inout vec3 pixelColor, vec3 positionWS, vec3 posRelativeToCamera)
{
    vec4 fog = GetCubemapFog(positionWS, posRelativeToCamera);
    pixelColor = mix(pixelColor, fog.rgb, fog.a);
}

uniform sampler3D g_tFogVolume; // reserved

// The volumetric fog between the camera and a position: the light it scatters in rgb, what it lets through in a.
// The volume is a grid of the screen, with slices at near + range * w^2 along the view direction.
vec4 GetVolumetricFog(vec3 positionWS)
{
    vec4 clipPos = g_matWorldToProjection * vec4(positionWS, 1.0);
    float viewDepth = dot(positionWS - g_vCameraPositionWs, g_vCameraDirWs);

    vec3 volumeCoords;
    volumeCoords.xy = clipPos.xy / clipPos.w * 0.5 + 0.5;
    volumeCoords.z = sqrt(saturate((viewDepth - g_vVolumetricFogNearRange.x) / g_vVolumetricFogNearRange.y));

    return textureLod(g_tFogVolume, volumeCoords, 0.0);
}

void ApplyVolumetricFog(inout vec3 pixelColor, vec3 positionWS)
{
    vec4 fog = GetVolumetricFog(positionWS);
    pixelColor = fog.rgb + pixelColor * fog.a;
}

void ApplyFog(inout vec3 pixelColor, vec3 positionWS)
{
    if (g_bFogEnabled)
    {
        vec3 cameraCenteredPixelPos = positionWS - g_vCameraPositionWs;

        if (g_bFogTypeEnabled.y)
        {
            #if (FOG_GRADIENT_TEXTURE)
                // The texture holds the start and the height range of the fog, there is nothing to cull by
                bool bPixelHasGradientFog = true;
            #elif (FOG_CULLING_BELOW_HEIGHT)
                float horizontalDistanceQuadratic = length_squared(cameraCenteredPixelPos.xy);
                bool bPixelHasGradientFog = (horizontalDistanceQuadratic > g_vGradientFogCullingParams.x) && (positionWS.z < g_vGradientFogCullingParams.y);
            #elif (FOG_CULLING_FIXED_HEIGHT_SIDE)
                float horizontalDistanceQuadratic = length_squared(cameraCenteredPixelPos.xy);
                bool bPixelHasGradientFog = (horizontalDistanceQuadratic > g_vGradientFogCullingParams.x) || (positionWS.z > g_vGradientFogCullingParams.y);
            #else
                // Skips the pixels where the fog is zero anyway: nearer than the start, or outside of the height range
                float distanceQuadratic = GetGradientFogDistanceSquared(cameraCenteredPixelPos);
                bool bPixelHasGradientFog = (distanceQuadratic > g_vGradientFogCullingParams.x) && IsGradientFogHeightInRange(positionWS.z);
            #endif
            if (bPixelHasGradientFog)
            {
                ApplyGradientFog(pixelColor, positionWS, cameraCenteredPixelPos);
            }
        }

        if (g_bFogTypeEnabled.z)
        {
            float distanceQuadratic = length_squared(cameraCenteredPixelPos);
            bool bPixelPastCubeFogStart = distanceQuadratic > g_vCubeFogCullingParams_ExposureBias_MaxOpacity.x;

            #if (FOG_CUBE_BLEND_MAX)
                bool bPixelHasCubemapFog = bPixelPastCubeFogStart || (positionWS.z > g_vCubeFogCullingParams_ExposureBias_MaxOpacity.y);
            #else
                // Skips the pixels where the fog is zero anyway, as for the gradient fog
                float cubeFogHeightScale = g_vCubeFog_Height_Offset_Scale_Exponent_Log2Mip.y;
                bool bPixelInCubeFogHeight = IsFogHeightInRange(positionWS.z, cubeFogHeightScale, g_vCubeFogCullingParams_ExposureBias_MaxOpacity.y);
                bool bPixelHasCubemapFog = bPixelPastCubeFogStart && bPixelInCubeFogHeight;
            #endif
            if (bPixelHasCubemapFog)
            {
                ApplyCubemapFog(pixelColor, positionWS, cameraCenteredPixelPos);
            }
        }

        #if (!FOG_NO_VOLUMETRIC)
            if (g_bFogTypeEnabled.x)
            {
                ApplyVolumetricFog(pixelColor, positionWS);
            }
        #endif
    }
}

// Fog attenuation factor for additive/mod2x contributions, which must fade with fog density rather
// than tint toward the fog color (tinting would add the fog color to the scene). Every pixel takes it,
// without the culling of ApplyFog().
float GetFogAttenuation(vec3 positionWS)
{
    if (!g_bFogEnabled)
    {
        return 1.0;
    }

    vec3 cameraCenteredPixelPos = positionWS - g_vCameraPositionWs;
    float attenuation = 1.0;

    if (g_bFogTypeEnabled.y)
    {
        attenuation *= 1.0 - GetGradientFog(positionWS, cameraCenteredPixelPos).a;
    }

    if (g_bFogTypeEnabled.z)
    {
        attenuation *= 1.0 - GetCubemapFog(positionWS, cameraCenteredPixelPos).a;
    }

    #if (!FOG_NO_VOLUMETRIC)
        if (g_bFogTypeEnabled.x)
        {
            attenuation *= GetVolumetricFog(positionWS).a;
        }
    #endif

    return saturate(attenuation);
}

// Mod2x blending darkens and brightens what is behind it around a gray of 0.5, so the fog fades it out
// instead of tinting it, faster for a darker fog. A saturated fog also lifts the color towards its own hue.
vec4 ApplyFogLayerMod2x(vec4 color, vec4 fog)
{
    float minFogColor = Min3(fog.rgb);
    float maxFogColor = Max3(fog.rgb);
    float saturation = maxFogColor == 0.0 ? 0.0 : (maxFogColor - minFogColor) / maxFogColor;

    vec3 liftedColor = max(color.rgb, fog.rgb * normalize(max(fog.rgb, vec3(0.0003))) * 1.06);
    float fadeExponent = mix(1.0, 2.0, pow(1.0 - saturate((minFogColor + maxFogColor) * 0.5), 8.0));

    color.rgb = mix(color.rgb * 2.0, liftedColor, saturation * fog.a) * 0.5;
    color.a *= 1.0 - pow(fog.a, fadeExponent);
    return color;
}

// Fog for mod2x blended surfaces. Every pixel takes it, without the culling of ApplyFog().
void ApplyFogMod2x(inout vec4 color, vec3 positionWS)
{
    if (!g_bFogEnabled)
    {
        return;
    }

    vec3 cameraCenteredPixelPos = positionWS - g_vCameraPositionWs;

    if (g_bFogTypeEnabled.y)
    {
        color = ApplyFogLayerMod2x(color, GetGradientFog(positionWS, cameraCenteredPixelPos));
    }

    if (g_bFogTypeEnabled.z)
    {
        color = ApplyFogLayerMod2x(color, GetCubemapFog(positionWS, cameraCenteredPixelPos));
    }
}

#endif
