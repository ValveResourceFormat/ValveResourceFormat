#version 460

#ifndef VRF_FOG_INCLUDE
#define VRF_FOG_INCLUDE

#include "ViewConstants.slang"
#include "utils.slang"

// Fog pixel shaders.

// Per-material
uniform bool g_bFogEnabled = true;

#define USE_VOLUMETRIC_FOG 0
#define USE_SPHERICAL_VIGNETTE_TERRIBLEIDEA 0

void ApplyGradientFog(inout vec3 pixelColor, vec3 positionWS, vec3 cameraCenteredPixelPos)
{
    vec2 gradientFogComponents; // x for View Space depth, y for height in World Space
    gradientFogComponents.x = length(cameraCenteredPixelPos);
    gradientFogComponents.y = positionWS.z;

    gradientFogComponents = pow( saturate(gradientFogComponents * g_vGradientFogBiasAndScale.zw + g_vGradientFogBiasAndScale.xy), m_vGradientFogExponents );

    float gradientFogBlend = gradientFogComponents.x * gradientFogComponents.y * g_vGradientFogColor_Opacity.a;

    pixelColor = mix(pixelColor, g_vGradientFogColor_Opacity.rgb, gradientFogBlend);
}

// ExposureBias is actually a separate param, but Z is unused and exposurebias stupidly only uses x

uniform samplerCube g_tFogCubeTexture;

void ApplyCubemapFog(inout vec3 pixelColor, vec3 positionWS, vec3 posRelativeToCamera)
{
    vec2 cubeFogComponents;
    cubeFogComponents.x = pow(ClampToPositive(length(posRelativeToCamera.xy) * g_vCubeFog_Offset_Scale_Bias_Exponent.y + g_vCubeFog_Offset_Scale_Bias_Exponent.x), g_vCubeFog_Offset_Scale_Bias_Exponent.w);
    cubeFogComponents.y = pow(ClampToPositive(positionWS.z * g_vCubeFog_Height_Offset_Scale_Exponent_Log2Mip.y + g_vCubeFog_Height_Offset_Scale_Exponent_Log2Mip.x), g_vCubeFog_Height_Offset_Scale_Exponent_Log2Mip.z);

    float cubemapFogBlend = saturate(cubeFogComponents.x) * saturate(cubeFogComponents.y);

    vec3 fogCoords = normalize((vec4(normalize(posRelativeToCamera), 0.0) * g_matvCubeFogSkyWsToOs).xyz);
    float cubemapFogLod = saturate( 1.0 - cubemapFogBlend * g_vCubeFog_Offset_Scale_Bias_Exponent.z ) * g_vCubeFog_Height_Offset_Scale_Exponent_Log2Mip.w;

    float cubemapFogOpacity = saturate(cubemapFogBlend) * g_vCubeFogCullingParams_ExposureBias_MaxOpacity.a;

    vec3 cubemapFogColor = textureLod(g_tFogCubeTexture, fogCoords.xyz, cubemapFogLod).rgb;
    cubemapFogColor *= g_vCubeFogCullingParams_ExposureBias_MaxOpacity.z;  //* g_vCubeFog_ExposureBias.x;
    pixelColor = mix(pixelColor, cubemapFogColor, cubemapFogOpacity);
}


#if (USE_VOLUMETRIC_FOG == 1) && 0
// I have all the shader code for volumetric fog, but it would be HARD to implement. Disabled for now.
uniform vec4 g_vVolFog_VolumeScale_Shift_Near_Range;
uniform vec4 g_vVolFogDitherScaleBias;
uniform vec4 g_vVolFogPostWorldToFrustumScale;
uniform vec4 g_vVolFogPostWorldToFrustumBias;
uniform mat4 g_mVolFogFromWorld; // original code is an array of length 2, and the code uses address [1]
uniform sampler3D g_tFogVolume;
uniform sampler2D g_tBlueNoise;

void ApplyVolumetricFog( inout vec3 PixelColor, vec3 positionWS )
{
    vec3 blueNoise = texture(g_tBlueNoise, gl_FragCoord.xy * g_vScreenSpaceDitherParams.zz + g_vScreenSpaceDitherParams.xy ) );
	vec3 volFogWorldPosJittered = positionWS + (blueNoise * g_vVolFogDitherScaleBias.xxx + g_vVolFogDitherScaleBias.yyy);

	vec4 projectedWorldPos = vec4(volFogWorldPosJittered, 1.0 ) * g_mVolFogFromWorld[0];

	vec3 frustumProjection = vec3(projectedWorldPos.xy / projectedWorldPos.ww, projectedWorldPos.w );
    frustumProjection = frustumProjection * g_vVolFogPostWorldToFrustumScale.xyz + g_vVolFogPostWorldToFrustumBias.xyz;
	vec4 volFogCoords = vec4( frustumProjection.xy, sqrt(ClampToPositive(frustumProjection.z)), ProjectedWorldPos.w ); //coords for the volfog sample are xyw in asm and xyz in vulkan

	vec4 volumetricFogColor = texture( g_tFogVolume, volFogCoords);
	PixelColor = ( PixelColor * volumetricFogColor.a ) + volumetricFogColor.rgb;
}
#endif

#if (USE_SPHERICAL_VIGNETTE_TERRIBLEIDEA == 42069)
// Spherical vignette is a bad idea, plain and simple. Would destroy visibility on maps that use it, since it's usually controlled by code.
vec4 g_vSphericalVignetteBiasAndScale;
vec4 g_vSphericalVignetteOrigin_Exponent;
vec4 g_vSphericalVignetteColor_Opacity;

void ApplySphericalVignette( inout vec3 pixelColor, vec3 worldPositionLightingOffset)
{
	float sphericalVignetteLinear = saturate(g_vSphericalVignetteBiasAndScale.y * distance(worldPositionLightingOffset, g_vSphericalVignetteOrigin_Exponent.xyz) + g_vSphericalVignetteBiasAndScale.x);
	float sphericalVignetteBlend = pow(sphericalVignetteLinear, g_vSphericalVignetteOrigin_Exponent.w);
	PixelColor = mix(pixelColor, g_vSphericalVignetteColor_Opacity.rgb, g_vSphericalVignetteColor_Opacity.a * sphericalVignetteBlend);
}
#endif

void ApplyFog(inout vec3 pixelColor, vec3 positionWS)
{
    if (g_bFogEnabled)
    {

#if (USE_VOLUMETRIC_FOG == 1)
		if (g_bFogTypeEnabled.x)
		{
    	    ApplyVolumetricFog(pixelColor, LightingPos);
		}
#endif

        vec3 cameraCenteredPixelPos = positionWS - g_vCameraPositionWs;

        if (g_bFogTypeEnabled.y)
        {
            float horizontalDistanceQuadratic = dot(cameraCenteredPixelPos.xy, cameraCenteredPixelPos.xy);

            bool bPixelHasGradientFog = (horizontalDistanceQuadratic > g_vGradientFogCullingParams.x) || (positionWS.z > g_vGradientFogCullingParams.y);
            if (bPixelHasGradientFog)
            {
                ApplyGradientFog(pixelColor, positionWS, cameraCenteredPixelPos);
            }
        }

        if (g_bFogTypeEnabled.z)
        {
            float distanceQuadratic = dot(cameraCenteredPixelPos, cameraCenteredPixelPos);

            bool bPixelHasCubemapFog = (distanceQuadratic > g_vCubeFogCullingParams_ExposureBias_MaxOpacity.x) || (positionWS.z > g_vCubeFogCullingParams_ExposureBias_MaxOpacity.y);
            if (bPixelHasCubemapFog)
            {
                ApplyCubemapFog(pixelColor, positionWS, cameraCenteredPixelPos);
            }
        }

#if (USE_SPHERICAL_VIGNETTE_TERRIBLEIDEA == 42069)
		// Spherical Vignette (Exclusive to Half-Life: Alyx. Used in a1_intro_world and startup, maybe others)
        //if (g_bFogTypeEnabled.w)
        //{
        //    ApplySphericalVignette(pixelColor, LightingPos);
        //}
#endif

    }
}

#endif
