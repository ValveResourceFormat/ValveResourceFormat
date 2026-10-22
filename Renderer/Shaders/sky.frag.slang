#version 460

#include "common/utils.slang"
#include "common/ViewConstants.slang"

in vec3 vSkyLookupInterpolant;
out vec4 vColor;

#define VRF_TEXTURE_FORMAT_YCOCG 2
#define VRF_TEXTURE_FORMAT_RGBM_DXT 4
#define VRF_TEXTURE_FORMAT_RGBM 5

// Core: 0="HDR (float16)", 1="RGB (8-bit uncompressed)", 2="YCoCg (dxt compressed)", 3="Dxt1 (LDR)", 4="RGBM (dxt compressed)", 5="RGBM (8-bit uncompressed)"
#define F_TEXTURE_FORMAT 0 // sky

// hlvr: 0="HDR (float16)", 1="RGB (8-bit uncompressed)", 2="YCoCg (dxt compressed)", 3="Dxt1 (LDR)", 4="RGBM (dxt compressed)", 5="RGBM (8-bit uncompressed)", 6="BC6H (HDR compressed - recommended)"
// cs2: 0="BC6H (HDR compressed - recommended)", 1="Dxt1 (LDR)", 2="HDR (float16)", 3="RGB (8-bit uncompressed)", 4="YCoCg (dxt compressed)", 5="RGBM (dxt compressed)", 6="RGBM (8-bit uncompressed)"
#define F_TEXTURE_FORMAT2 0 // sky

#if (F_TEXTURE_FORMAT2 > 0)
    #define TextureFormat F_TEXTURE_FORMAT2
#else
    #define TextureFormat F_TEXTURE_FORMAT
#endif

#if (TextureFormat == VRF_TEXTURE_FORMAT_YCOCG)
    #define ENCODING_YCOCG
#elif (TextureFormat == VRF_TEXTURE_FORMAT_RGBM_DXT || TextureFormat == VRF_TEXTURE_FORMAT_RGBM)
    #define ENCODING_RGBM
#endif

uniform samplerCube g_tSkyTexture; // SrgbRead(true)

uniform float g_flBrightnessExposureBias;
uniform float g_flRenderOnlyExposureBias;
uniform vec3 g_vTint = vec3(1.0, 1.0, 1.0);

// 0 is no fog, 1 fogs the sky as far away as the far plane, 2 fogs it by its angle above the horizon
uniform int g_nSkyFogType;
uniform vec3 g_vSkyFogColor;
uniform float g_flSkyDistanceFog;

// Where the angular fog starts and ends below and above the horizon, as heights of the view direction
uniform vec4 g_vSkyFogMinMaxDots;
uniform bool g_bSkyMinAngularFog;

const float g_flToneMapScalarLinear = 1.0;


void main()
{
    vec3 vEyeToSkyDirWs = normalize(vSkyLookupInterpolant);
    vec4 skyTexel = texture(g_tSkyTexture, vEyeToSkyDirWs);

#if defined(ENCODING_YCOCG)
    vColor.rgb = DecodeYCoCg(skyTexel);
    vColor.rgb = SrgbGammaToLinear(vColor.rgb);
#elif defined(ENCODING_RGBM)
    vColor.rgb = DecodeRGBM(skyTexel);
#else
    vColor.rgb = skyTexel.rgb;
#endif
    // Because we precalculate neither of these, we can at least save one exp2 by substituting them into one factor
    vColor.rgb *= exp2(g_flBrightnessExposureBias + g_flRenderOnlyExposureBias);
    vColor.rgb *= g_vTint;

    vColor.rgb = ClampToPositive(vColor.rgb);

    if (g_nSkyFogType == 1)
    {
        vColor.rgb = mix(vColor.rgb, g_vSkyFogColor, g_flSkyDistanceFog);
    }
    else if (g_nSkyFogType == 2)
    {
        float flHeight = vEyeToSkyDirWs.z;
        float flFog = 1.0 - saturate((flHeight - g_vSkyFogMinMaxDots.z) / max(0.001, g_vSkyFogMinMaxDots.w - g_vSkyFogMinMaxDots.z));

        if (g_bSkyMinAngularFog)
        {
            flFog = min(saturate((flHeight - g_vSkyFogMinMaxDots.y) / max(0.0001, g_vSkyFogMinMaxDots.x - g_vSkyFogMinMaxDots.y)), flFog);
        }

        vColor.rgb = mix(vColor.rgb, g_vSkyFogColor, flFog);
    }

    // Every render mode draws the sky as it is, shown as it is written without post processing
    if (g_iRenderMode != 0)
    {
        vColor.rgb = SrgbLinearToGamma(vColor.rgb);
    }

    //vColor.rgb += (vSkyLookupInterpolant * 0.5);
    vColor.a = 1.0;
}
