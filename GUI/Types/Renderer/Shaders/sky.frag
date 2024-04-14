#version 460

#include "common/utils.glsl"

in vec3 vSkyLookupInterpolant;
out vec4 vColor;

#define VRF_TEXTURE_FORMAT_YCOCG 2
#define VRF_TEXTURE_FORMAT_RGBM_DXT 4
#define VRF_TEXTURE_FORMAT_RGBM 5

// Core: 0="HDR (float16)", 1="RGB (8-bit uncompressed)", 2="YCoCg (dxt compressed)", 3="Dxt1 (LDR)", 4="RGBM (dxt compressed)", 5="RGBM (8-bit uncompressed)"
#define F_TEXTURE_FORMAT 0

// hlvr: 0="HDR (float16)", 1="RGB (8-bit uncompressed)", 2="YCoCg (dxt compressed)", 3="Dxt1 (LDR)", 4="RGBM (dxt compressed)", 5="RGBM (8-bit uncompressed)", 6="BC6H (HDR compressed - recommended)"
// cs2: 0="BC6H (HDR compressed - recommended)", 1="Dxt1 (LDR)", 2="HDR (float16)", 3="RGB (8-bit uncompressed)", 4="YCoCg (dxt compressed)", 5="RGBM (dxt compressed)", 6="RGBM (8-bit uncompressed)"
#define F_TEXTURE_FORMAT2 0

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

uniform samplerCube g_tSkyTexture;

uniform float g_flBrightnessExposureBias;
uniform float g_flRenderOnlyExposureBias;
uniform vec3 m_vTint = vec3(1.0, 1.0, 1.0);

const float g_flToneMapScalarLinear = 1.0;


void main()
{
    vec3 vEyeToSkyDirWs = normalize(vSkyLookupInterpolant);
    vec4 skyTexel = texture(g_tSkyTexture, vEyeToSkyDirWs);

#if defined(ENCODING_YCOCG)
    vColor.rgb = DecodeYCoCg(skyTexel);
    vColor.rgb = SrgbGammaToLinear(vColor.rgb);
#elif defined(ENCODING_RGBM)
    const float MaxRange = 6.0;
    vColor.rgb = skyTexel.rgb * (skyTexel.a * MaxRange);
    vColor.rgb *= vColor.rgb;
#else
    vColor.rgb = skyTexel.rgb;
#endif
    vColor.rgb *= (1.0 + g_flBrightnessExposureBias);
    vColor.rgb *= (1.0 + g_flRenderOnlyExposureBias);

    vColor.rgb = SrgbLinearToGamma(vColor.rgb);
    vColor.rgb *= m_vTint;
    vColor.rgb = ClampToPositive(vColor.rgb);
    vColor.rgb *= g_flToneMapScalarLinear;

    // Why do we do this?
    vColor.a = GetLuma(vColor.rgb);

    //vColor.rgb += (vSkyLookupInterpolant*0.5);
}
