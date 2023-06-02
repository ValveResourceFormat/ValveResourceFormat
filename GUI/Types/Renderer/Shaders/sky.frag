#version 330

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
uniform vec3 g_vTint = vec3(1.0, 1.0, 1.0);

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

    vec3 vLinearSegment =  color / vec3(12.92);
    vec3 vExpSegment = pow((color / vec3(1.055)) + vec3(0.0521327), vec3(2.4));

    const float cap = 0.04045;
	float select = R > cap ? vExpSegment.x : vLinearSegment.x;
	float select1 = G > cap ? vExpSegment.y : vLinearSegment.y;
	float select2 = B > cap ? vExpSegment.z : vLinearSegment.z;

    return vec3(select, select1, select2);
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

    //vColor.rgb *= (1.0 + g_flBrightnessExposureBias);
    //vColor.rgb *= (1.0 + g_flRenderOnlyExposureBias);
    vColor.rgb *= g_vTint;
    vColor.rgb = max(vColor.rgb, vec3(0.0));
    vColor.rgb *= g_flToneMapScalarLinear;

    vColor.a = dot(vColor.rgb, vec3(0.3, 0.59, 0.11));

    //vColor.rgb += (vSkyLookupInterpolant*0.5);
}
