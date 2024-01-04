#version 460

#include "common/utils.glsl"

in vec3 vSkyLookupInterpolant;
out vec4 vColor;

// Core: 0="HDR (float16)", 1="RGB (8-bit uncompressed)", 2="YCoCg (dxt compressed)", 3="Dxt1 (LDR)", 4="RGBM (dxt compressed)", 5="RGBM (8-bit uncompressed)"
#define F_TEXTURE_FORMAT 0
// hlvr: 0="HDR (float16)", 1="RGB (8-bit uncompressed)", 2="YCoCg (dxt compressed)", 3="Dxt1 (LDR)", 4="RGBM (dxt compressed)", 5="RGBM (8-bit uncompressed)", 6="BC6H (HDR compressed - recommended)"
// cs2: 0="BC6H (HDR compressed - recommended)", 1="Dxt1 (LDR)", 2="HDR (float16)", 3="RGB (8-bit uncompressed)", 4="YCoCg (dxt compressed)", 5="RGBM (dxt compressed)", 6="RGBM (8-bit uncompressed)"
// TODO: differently ordered formats in cs2
#define F_TEXTURE_FORMAT2 0

#if (F_TEXTURE_FORMAT == 2 || F_TEXTURE_FORMAT2 == 2)
    #define ENCODING_YCOCG
#elif (F_TEXTURE_FORMAT == 4 || F_TEXTURE_FORMAT2 == 4 || F_TEXTURE_FORMAT == 5 || F_TEXTURE_FORMAT2 == 5)
    #define ENCODING_RGBM
#endif

uniform samplerCube g_tSkyTexture;

uniform float g_flBrightnessExposureBias;
uniform float g_flRenderOnlyExposureBias;
uniform vec3 m_vTint = vec3(1.0, 1.0, 1.0);

const float g_flToneMapScalarLinear = 1.0;


vec3 DecodeYCoCg(vec4 YCoCg)
{
    float scale = (YCoCg.z * (255.0 / 8.0)) + 1.0;
    float Co = (YCoCg.x + (-128.0 / 255.0)) / scale;
    float Cg = (YCoCg.y + (-128.0 / 255.0)) / scale;
    float Y = YCoCg.w;

    float R = Y + Co - Cg;
    float G = Y + Cg;
    float B = Y - Co - Cg;

    vec3 color = vec3(R, G, B);

    return SrgbGammaToLinear(color);
}

void main()
{
    vec3 vEyeToSkyDirWs = normalize(vSkyLookupInterpolant);
    vec4 skyTexel = texture(g_tSkyTexture, vEyeToSkyDirWs);

#if defined(ENCODING_YCOCG)
    vColor.rgb = DecodeYCoCg(skyTexel);
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
