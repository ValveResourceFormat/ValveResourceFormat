#version 460

#ifndef VRF_SOURCE1_COMMON_PS_INCLUDE
#define VRF_SOURCE1_COMMON_PS_INCLUDE

#include "../common/utils.slang"
#include "../common/view_constants.slang"
#include "../common/fog.slang"

// 1 when the detail texture of every blend mode but 0 holds sRGB colors, 0 when only mode 1 does
#ifndef SOURCE1_DETAIL_SRGB_ALL_MODES
    #define SOURCE1_DETAIL_SRGB_ALL_MODES 1
#endif

out vec4 outputColor;

uniform int F_ALPHA_TEST;
uniform int F_TRANSLUCENT;
uniform int F_DETAIL;
uniform float g_flAlphaTestReference = 0.7;
uniform float g_flOpacityScale = 1.0;
uniform float g_flDetailScale = 4.0;
uniform float g_flDetailBlendFactor = 1.0;
uniform vec3 g_vDetailTint = vec3(1.0);
uniform int g_nDetailBlendMode;
uniform int F_SELF_ILLUM;
uniform int F_SELF_ILLUM_MASK;
uniform int F_BASE_ALPHA_ENVMAP_MASK;
uniform int F_SELF_ILLUM_ENVMAPMASK_ALPHA;
uniform int F_SELF_ILLUM_FRESNEL;
uniform vec3 g_vSelfIllumTint = vec3(1.0);
// Scale, bias, exponent and brightness of the fresnel term of the self illumination
uniform vec4 g_vSelfIllumFresnel = vec4(1.0, 0.0, 1.0, 1.0);

// A tint written in gamma space, which stays as it is when it is brighter than one
vec3 Source1TintGammaToLinear(vec3 tint)
{
    return mix(SrgbGammaToLinear(tint), tint, greaterThan(tint, vec3(1.0)));
}

// The detail texture is read without sRGB conversion, as only some blend modes hold colors
vec4 SampleSource1Detail(sampler2D detailTexture, vec2 texCoord)
{
    if (F_DETAIL == 0)
    {
        return vec4(1.0);
    }

    vec4 detailColor = texture(detailTexture, texCoord * g_flDetailScale);

#if (SOURCE1_DETAIL_SRGB_ALL_MODES == 1)
    bool isColor = g_nDetailBlendMode != 0;
#else
    bool isColor = g_nDetailBlendMode == 1;
#endif

    if (isColor)
    {
        detailColor.rgb = SrgbGammaToLinear(detailColor.rgb);
    }

    detailColor.rgb *= g_vDetailTint;
    return detailColor;
}

// The detail blend modes of TextureCombine that act before lighting
vec4 Source1TextureCombine(vec4 baseColor, vec4 detailColor, int mode, float blendFactor)
{
    if (mode == 0)
    {
        baseColor.rgb *= mix(vec3(1.0), 2.0 * detailColor.rgb, blendFactor);
    }
    else if (mode == 1)
    {
        baseColor.rgb += blendFactor * detailColor.rgb;
    }
    else if (mode == 2)
    {
        baseColor.rgb = mix(baseColor.rgb, detailColor.rgb, blendFactor * detailColor.a);
    }
    else if (mode == 3)
    {
        baseColor = mix(baseColor, detailColor, blendFactor);
    }
    else if (mode == 4)
    {
        baseColor.rgb = mix(baseColor.rgb, detailColor.rgb, blendFactor * (1.0 - baseColor.a));
        baseColor.a = detailColor.a;
    }
    else if (mode == 7)
    {
        vec3 pattern = vec3(mix(detailColor.r, detailColor.a, baseColor.a));
        baseColor.rgb *= mix(vec3(1.0), 2.0 * pattern, blendFactor);
    }
    else if (mode == 8)
    {
        baseColor = mix(baseColor, baseColor * detailColor, blendFactor);
    }
    else if (mode == 9)
    {
        baseColor.a = mix(baseColor.a, baseColor.a * detailColor.a, blendFactor);
    }

    return baseColor;
}

vec4 ApplySource1Detail(vec4 baseColor, vec4 detailColor)
{
    if (F_DETAIL == 0)
    {
        return baseColor;
    }

    return Source1TextureCombine(baseColor, detailColor, g_nDetailBlendMode, g_flDetailBlendFactor);
}

// The detail blend modes that add the detail to the lit color
vec3 ApplySource1DetailPostLighting(vec3 litColor, vec4 detailColor)
{
    if (F_DETAIL == 0)
    {
        return litColor;
    }

    if (g_nDetailBlendMode == 5)
    {
        litColor += g_flDetailBlendFactor * detailColor.rgb;
    }
    else if (g_nDetailBlendMode == 6)
    {
        float high = g_flDetailBlendFactor - 0.5;
        float multiply = (high >= 0.0) ? 1.0 / g_flDetailBlendFactor : 4.0 * g_flDetailBlendFactor;
        float add = (high >= 0.0) ? 1.0 - multiply : -0.5 * multiply;
        litColor += saturate(multiply * detailColor.rgb + add);
    }

    return litColor;
}

// Blends the lit color toward the tinted albedo by the alpha of the base texture or by a mask texture
vec3 ApplySource1SelfIllum(vec3 color, vec3 albedo, float baseAlpha, sampler2D maskTexture, vec2 texCoord)
{
    if (F_SELF_ILLUM == 0)
    {
        return color;
    }

    vec3 illumination = vec3(baseAlpha);

    if (F_SELF_ILLUM_MASK != 0)
    {
        illumination = texture(maskTexture, texCoord).rgb;
    }

    return mix(color, g_vSelfIllumTint * albedo, illumination);
}

// Blends the lit color toward the tinted albedo by the alpha of the environment map mask, scaled up so that the top of
// the range overbrightens the glow
vec3 ApplySource1SelfIllumFromMaskAlpha(vec3 color, vec3 albedo, sampler2D maskTexture, vec2 texCoord)
{
    if (F_SELF_ILLUM_ENVMAPMASK_ALPHA == 0)
    {
        return color;
    }

    float amount = 8.0 * texture(maskTexture, texCoord).a;

    return max(0.0, 1.0 - amount) * color + amount * g_vSelfIllumTint * albedo;
}

// Blends the lit color toward the brightened, tinted albedo by the base alpha, faded by the angle between the vertex
// normal and the eye
vec3 ApplySource1SelfIllumFresnel(vec3 color, vec3 albedo, float baseAlpha, vec3 vertexNormal, vec3 eyeDirection)
{
    float fresnel = pow(saturate(dot(vertexNormal, eyeDirection)), g_vSelfIllumFresnel.z) * g_vSelfIllumFresnel.x + g_vSelfIllumFresnel.y;
    vec3 glow = g_vSelfIllumTint * albedo * g_vSelfIllumFresnel.w;

    return mix(color, glow, baseAlpha * saturate(fresnel));
}

// The opacity of a surface from the alpha of its base texture. Only translucent and alpha tested materials read the
// texture alpha, and not when self illumination or the environment map mask use it.
float GetSource1Opacity(float baseAlpha, float vertexAlpha)
{
    bool alphaIsMask = F_SELF_ILLUM != 0 || F_BASE_ALPHA_ENVMAP_MASK != 0;
    bool readsBaseAlpha = (F_TRANSLUCENT != 0 || F_ALPHA_TEST != 0) && !alphaIsMask;
    float alpha = readsBaseAlpha ? baseAlpha : 1.0;

    return alpha * g_flOpacityScale * vertexAlpha;
}

// Cuts out an alpha tested surface, which is then opaque
float ApplySource1AlphaTest(float alpha)
{
    if (F_ALPHA_TEST != 0)
    {
        if (alpha < g_flAlphaTestReference)
        {
            discard;
        }

        return 1.0;
    }

    return alpha;
}

#endif
