#version 460

#include "common_ps.slang"
#include "common_envmap_ps.slang"
#include "common_rendermodes_lighting_ps.slang"
#include "../common/shadowmapping.slang"
#include "common_local_lights_ps.slang"

// The power of the angle term of the environment map reflection
#ifndef SOURCE1_FRESNEL_POWER
    #define SOURCE1_FRESNEL_POWER 5.0
#endif

in vec3 vFragPosition;
in vec3 vNormalOut;
in vec4 vTangentOut;
in vec2 vTexCoordOut;
in vec2 vBumpTexCoordOut;
#if (SOURCE1_BUMPMAP2 == 1)
in vec2 vBumpTexCoord2Out;
#endif
in vec2 vEnvMapMaskTexCoordOut;
in vec2 vBlendMaskTexCoordOut;
in vec2 vLightmapUVOut;
in vec4 vVertexColorOut;
in float vBlendOut;

#if (SOURCE1_FOUR_WAY_BLEND == 1)
in vec4 vBlendValuesOut;

uniform sampler2D g_tColor3; // SrgbRead(true) Sampler(UserConfig)
uniform sampler2D g_tColor4; // SrgbRead(true) Sampler(UserConfig)

// The scale of the texture coordinates of the second to fourth textures
uniform vec2 g_vUvScale2 = vec2(1.0);
uniform vec2 g_vUvScale3 = vec2(1.0);
uniform vec2 g_vUvScale4 = vec2(1.0);
// The luminance of each texture, between which its weight rises
uniform vec2 g_vLumRange1 = vec2(0.0, 1.0);
uniform vec2 g_vLumRange2 = vec2(0.0, 1.0);
uniform vec2 g_vLumRange3 = vec2(0.0, 1.0);
uniform vec2 g_vLumRange4 = vec2(0.0, 1.0);
// The range of the weight of a layer in which it blends in
uniform vec2 g_vBlendRange2 = vec2(0.0, 1.0);
uniform vec2 g_vBlendRange3 = vec2(0.0, 1.0);
uniform vec2 g_vBlendRange4 = vec2(0.0, 1.0);
// How much the luminance of the second to fourth textures weighs against the layers below
uniform vec3 g_vLumBlendFactors = vec3(1.0);
// How much the bump map carries into the second to fourth layers
uniform vec3 g_vBumpBlendFactors = vec3(1.0);
// The detail blend factor of each of the four layers
uniform vec4 g_vDetailBlendFactors = vec4(1.0);
// 0 blends the third and fourth textures over the layers below, 1 multiplies the layers by twice the texture
uniform int g_nTexture3BlendMode;
uniform int g_nTexture4BlendMode;
#endif

uniform sampler2D g_tColor;// SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tColor2; // SrgbRead(true) Sampler(UserConfig)
uniform sampler2D g_tDetail; // Sampler(UserConfig)
uniform sampler2D g_tSelfIllumMask; // Sampler(UserConfig)
uniform sampler2D g_tBlendModulate; // Sampler(UserConfig)
uniform sampler2D g_tNormal; // Sampler(UserConfig)
uniform sampler2D g_tNormal2; // Sampler(UserConfig)
#if (SOURCE1_BUMPMASK == 1)
uniform sampler2D g_tBumpMask; // Sampler(UserConfig)
#endif
uniform sampler2D g_tLightWarp;
uniform sampler2D g_tLightmap;
uniform sampler2D g_tLightmapBump1;
uniform sampler2D g_tLightmapBump2;
uniform sampler2D g_tLightmapBump3;

uniform int F_BLEND_MODULATE;
// The blend of the two layers comes from the blend modulate texture alone, not from the vertices
uniform int F_MASKED_BLENDING;
// A layer whose normal map alpha masks the environment map can switch the reflection off
uniform int F_BASE_TEXTURE_NO_ENVMAP;
uniform int F_BASE_TEXTURE2_NO_ENVMAP;
uniform int F_BUMPMAP;
uniform int F_SSBUMP;
uniform int F_BUMPED_LIGHTMAP;
uniform int F_LIGHTWARP;
// The tint multiplies the light, in the color space it was written in
uniform vec3 g_vColorTint = vec3(1.0);
uniform float g_flLightmapScale = 1.0;
// How much the light on the surface dims the environment map: 0 for none, 1 for black where there is no light
uniform float g_flEnvMapLightScale = 0.0;
// The light below x counts as none, and the light is then multiplied by y
uniform vec2 g_vEnvMapLightScaleRange = vec2(0.0, 1.0);
// When not zero, the base texture is projected along the three world axes instead of using the texture coordinates
uniform float g_flSeamlessScale = 0.0;

// The detail blend mode of a detail texture that holds the weights of the bumped lightmaps
#define SOURCE1_DETAIL_BLEND_BUMP_WEIGHTS 10

// The base textures and the bump map share the projection. The scale applies to the texture coordinates of the
// projection too.
vec4 SampleSource1SurfaceTexture(sampler2D surfaceTexture, vec2 texCoord, vec2 scale)
{
    if (g_flSeamlessScale == 0.0)
    {
        return texture(surfaceTexture, texCoord * scale);
    }

    vec3 position = vFragPosition * g_flSeamlessScale;
    vec3 normal = normalize(vNormalOut);
    vec3 weights = normal * normal;

    return texture(surfaceTexture, position.xz * scale) * weights.y
        + texture(surfaceTexture, position.zy * scale) * weights.x
        + texture(surfaceTexture, position.xy * scale) * weights.z;
}

vec4 SampleSource1SurfaceTexture(sampler2D surfaceTexture, vec2 texCoord)
{
    return SampleSource1SurfaceTexture(surfaceTexture, texCoord, vec2(1.0));
}

// Cubic B-spline filtering of a lightmap page from four bilinear taps, to hide the luxel steps of the coarse lightmaps.
// It reads up to two luxels beyond the sampled one, which stays inside the repeated border of a face.
vec4 SampleSource1Lightmap(sampler2D page, vec2 uv)
{
    vec2 size = vec2(textureSize(page, 0));
    vec2 texel = uv * size - 0.5;
    vec2 base = floor(texel);
    vec2 f = texel - base;
    vec2 f2 = f * f;
    vec2 f3 = f2 * f;

    vec2 w0 = (-f3 + 3.0 * f2 - 3.0 * f + 1.0) / 6.0;
    vec2 w1 = (3.0 * f3 - 6.0 * f2 + 4.0) / 6.0;
    vec2 w2 = (-3.0 * f3 + 3.0 * f2 + 3.0 * f + 1.0) / 6.0;
    vec2 w3 = f3 / 6.0;

    vec2 weight0 = w0 + w1;
    vec2 weight1 = w2 + w3;

    vec2 uv0 = (base - 0.5 + w1 / weight0) / size;
    vec2 uv1 = (base + 1.5 + w3 / weight1) / size;

    vec4 light = textureLod(page, uv0, 0.0) * weight0.x * weight0.y;
    light += textureLod(page, vec2(uv1.x, uv0.y), 0.0) * weight1.x * weight0.y;
    light += textureLod(page, vec2(uv0.x, uv1.y), 0.0) * weight0.x * weight1.y;
    light += textureLod(page, vec2(uv1.x, uv1.y), 0.0) * weight1.x * weight1.y;

    return light;
}

// How much each of the three bumped lightmaps lights a bump mapped surface, summing to 1. tangentNormal is the normal
// of the bump map in tangent space, or for a self shadowed bump map the weights of the three lightmaps.
vec3 GetSource1BumpedLightmapWeights(vec3 tangentNormal, vec4 detailColor)
{
    if (F_SSBUMP != 0)
    {
        // A flat normal gives each weight 0.57735, where the lightmaps expect them to sum to 1
        return tangentNormal * 0.57735026;
    }

    vec3 weights;
    weights.x = saturate(dot(tangentNormal, SOURCE1_BUMP_BASIS[0]));
    weights.y = saturate(dot(tangentNormal, SOURCE1_BUMP_BASIS[1]));
    weights.z = saturate(dot(tangentNormal, SOURCE1_BUMP_BASIS[2]));
    weights *= weights;

    if (F_DETAIL != 0 && g_nDetailBlendMode == SOURCE1_DETAIL_BLEND_BUMP_WEIGHTS)
    {
        weights *= 2.0 * detailColor.rgb;
    }

    float sum = max(weights.x + weights.y + weights.z, 0.0001);

    return weights / sum;
}

// The light on a bump mapped surface, from the three bumped lightmaps. The alpha is the sun amount, weighted as the
// light is.
vec4 GetSource1BumpedLightmap(vec2 lightmapCoord, vec3 tangentNormal, vec4 detailColor)
{
    vec4 light1 = SampleSource1Lightmap(g_tLightmapBump1, lightmapCoord);
    vec4 light2 = SampleSource1Lightmap(g_tLightmapBump2, lightmapCoord);
    vec4 light3 = SampleSource1Lightmap(g_tLightmapBump3, lightmapCoord);
    vec3 weights = GetSource1BumpedLightmapWeights(tangentNormal, detailColor);

    return weights.x * light1 + weights.y * light2 + weights.z * light3;
}

// The light the dynamic lights add to the lightmap of a surface. A flat lightmap takes the light of each as it is. The
// bumped lightmaps take it along each of their directions, scaled by how much the light shines onto the surface.
vec3 GetSource1DynamicLightmap(vec3 position, vec3 geometricNormal, vec3 tangentNormal, vec4 detailColor)
{
    bool bumped = F_BUMPMAP != 0 && F_BUMPED_LIGHTMAP != 0;
    mat3 tangentFrame = mat3(1.0);
    vec3 weights = vec3(0.0);

    if (bumped)
    {
        tangentFrame = GetSource1TangentFrame(geometricNormal, vTangentOut, position, vBumpTexCoordOut);
        weights = GetSource1BumpedLightmapWeights(tangentNormal, detailColor);
    }

    vec3 light = vec3(0.0);

    for (uint lightIndex = g_nNumLights[0]; lightIndex < g_nNumLights[1]; lightIndex++)
    {
        if (!IsSource1DynamicLight(lightIndex))
        {
            continue;
        }

        vec3 lightDirection;
        vec3 lightColor = GetSource1DynamicLightColor(lightIndex, position, geometricNormal, lightDirection);

        if (!bumped)
        {
            light += lightColor;
            continue;
        }

        vec3 tangentDirection = vec3(dot(tangentFrame[0], lightDirection), dot(tangentFrame[1], lightDirection), dot(tangentFrame[2], lightDirection));
        float shine = max(tangentDirection.z, 0.001);
        vec3 basisLight = vec3(
            max(dot(tangentDirection, SOURCE1_BUMP_BASIS[0]), 0.0),
            max(dot(tangentDirection, SOURCE1_BUMP_BASIS[1]), 0.0),
            max(dot(tangentDirection, SOURCE1_BUMP_BASIS[2]), 0.0));

        light += lightColor * shine * dot(weights, basisLight);
    }

    return light;
}

// The lightmap with the direct sun taken out where the sun shadow cascades are in shadow. The alpha of the lightmap is
// how much the sun lit it directly, which times the sun color is the direct sun in lightmap units. Maps that do not
// store it have an alpha of 0 and keep their lightmap.
vec3 ApplySource1SunShadow(vec4 lightmap, vec3 position)
{
    float sunAmount = lightmap.a;

    if (sunAmount <= 0.0)
    {
        return lightmap.rgb;
    }

    float shadow = CalculateSunShadowMapVisibility(position);
    vec3 directSun = sunAmount * g_vSunColor.rgb;

    return max(lightmap.rgb - directSun * (1.0 - shadow), vec3(0.0));
}

// A self shadowed bump map holds weights, the others hold a signed normal
vec3 DecodeSource1BumpTexel(vec3 texel)
{
    if (F_SSBUMP != 0)
    {
        return texel;
    }

    return texel * 2.0 - 1.0;
}

// The world space normal of the bump map
vec3 GetSource1BumpedNormal(vec3 normal, vec3 tangentNormal)
{
    vec3 bumpNormal = tangentNormal;

    if (F_SSBUMP != 0)
    {
        bumpNormal = normalize(SOURCE1_BUMP_BASIS[0] * tangentNormal.x
            + SOURCE1_BUMP_BASIS[1] * tangentNormal.y
            + SOURCE1_BUMP_BASIS[2] * tangentNormal.z);
    }

    mat3 tangentFrame = GetSource1TangentFrame(normal, vTangentOut, vFragPosition, vBumpTexCoordOut);
    return normalize(tangentFrame * bumpNormal);
}

#if (SOURCE1_FOUR_WAY_BLEND == 1)
float SmoothStepRange(vec2 range, float value)
{
    float amount = saturate((value - range.x) / max(range.y - range.x, 0.0001));
    return amount * amount * (3.0 - 2.0 * amount);
}

// How much the second to fourth textures show over the layers below. The painted weight of a layer grows with the
// luminance of its texture against the luminance of the layers below.
vec3 GetSource1LayerBlends(vec3 color1, vec3 color2, vec3 color3, vec3 color4)
{
    float lum1 = SmoothStepRange(g_vLumRange1, dot(color1, LumaWeights));
    float lum2 = SmoothStepRange(g_vLumRange2, dot(color2, LumaWeights));
    float lum3 = SmoothStepRange(g_vLumRange3, dot(color3, LumaWeights));
    float lum4 = SmoothStepRange(g_vLumRange4, dot(color4, LumaWeights));

    float lum = mix(1.0 - lum1, lum2, g_vLumBlendFactors.x);
    float blend2 = SmoothStepRange(g_vBlendRange2, vBlendValuesOut.g * lum + vBlendValuesOut.g);
    float lumBelow = mix(lum1, lum2, blend2);

    lum = mix(1.0 - lumBelow, lum3, g_vLumBlendFactors.y);
    float blend3 = SmoothStepRange(g_vBlendRange3, vBlendValuesOut.b * lum + vBlendValuesOut.b);
    lumBelow = mix(lumBelow, lum3, blend3);

    lum = mix(1.0 - lumBelow, lum4, g_vLumBlendFactors.z);
    float blend4 = SmoothStepRange(g_vBlendRange4, vBlendValuesOut.a * lum + vBlendValuesOut.a);

    return vec3(blend2, blend3, blend4);
}

vec4 BlendSource1Layers(vec4 color1, vec4 color2, vec4 color3, vec4 color4, vec3 blends)
{
    vec4 color = mix(color1, color2, blends.x);

    if (g_nTexture3BlendMode == 0)
    {
        color = mix(color, color3, blends.y);
    }
    else if (g_nTexture3BlendMode == 1)
    {
        color *= mix(vec4(1.0), color3 + color3, blends.y);
    }

    if (g_nTexture4BlendMode == 0)
    {
        color = mix(color, color4, blends.z);
    }
    else if (g_nTexture4BlendMode == 1)
    {
        color *= mix(vec4(1.0), color4 + color4, blends.z);
    }

    return color;
}

float GetSource1LayerDetailBlendFactor(vec3 blends)
{
    float factor = mix(g_vDetailBlendFactors.x, g_vDetailBlendFactors.y, blends.x);
    factor = mix(factor, g_vDetailBlendFactors.z, blends.y);
    return mix(factor, g_vDetailBlendFactors.w, blends.z);
}

// The tangent space normal of the blended layers. A normal map has the second texture, and the layers without one
// flatten the normal by their bump blend factor.
vec3 BlendSource1LayerNormals(vec3 tangentNormal, vec3 blends)
{
    float bumpFactor = 1.0;

#if (SOURCE1_BUMPMAP2 == 1)
    vec4 normalTexel2 = texture(g_tNormal2, vBumpTexCoordOut * g_vUvScale2);
    tangentNormal = mix(tangentNormal, DecodeSource1BumpTexel(normalTexel2.xyz), blends.x);

    if (F_SSBUMP == 0)
    {
        tangentNormal = normalize(tangentNormal);
    }
#else
    bumpFactor = mix(bumpFactor, g_vBumpBlendFactors.x, blends.x);
#endif

    bumpFactor = mix(bumpFactor, g_vBumpBlendFactors.y, blends.y);
    bumpFactor = mix(bumpFactor, g_vBumpBlendFactors.z, blends.z);

    if (F_SSBUMP != 0)
    {
        return tangentNormal * bumpFactor;
    }

    return normalize(mix(vec3(0.0, 0.0, 1.0), tangentNormal, bumpFactor));
}
#endif

#if (SOURCE1_WORLD_TWO_TEXTURE_BLEND == 1)
// The detail alpha masks the base texture, which it multiplies
uniform int F_DETAIL_ALPHA_MASK_BASE_TEXTURE;

// The detail texture replaces the base color where its alpha is set. With the alpha mask it is instead multiplied by
// the doubled base color, which is only shown where the detail alpha is set.
vec3 BlendSource1WorldDetail(vec3 baseColor, vec4 detailColor)
{
    if (F_DETAIL == 0)
    {
        return baseColor;
    }

    if (F_DETAIL_ALPHA_MASK_BASE_TEXTURE != 0)
    {
        vec3 masked = saturate(saturate(baseColor * 2.0) * detailColor.a + (1.0 - detailColor.a));
        return masked * detailColor.rgb;
    }

    return mix(baseColor, detailColor.rgb, detailColor.a);
}
#endif

void main()
{
    vec4 baseColor = SampleSource1SurfaceTexture(g_tColor, vTexCoordOut);

    // The opacity and the self illumination use the alpha of the first texture, the environment map mask the blended one
    float baseAlpha = baseColor.a;
    float blendedAlpha = baseAlpha;
    float blend = 0.0;

#if (SOURCE1_BLEND_BY_VERTEX_ALPHA == 1)
    blend = vBlendOut;

    if (F_MASKED_BLENDING != 0)
    {
        blend = 0.5;
    }

#if (SOURCE1_BASE_TEXTURE2 == 1)
    // The blend modulate texture only applies to a surface that does not glow
    if (F_BLEND_MODULATE != 0 && F_SELF_ILLUM == 0)
    {
        vec4 modulate = texture(g_tBlendModulate, vBlendMaskTexCoordOut);

        if (F_MASKED_BLENDING != 0)
        {
            blend = modulate.g;
        }
        else
        {
            float low = saturate(modulate.g - modulate.r);
            float high = saturate(modulate.g + modulate.r);
            blend = smoothstep(low, max(high, low + 0.0001), blend);
        }
    }
#endif
#endif

#if (SOURCE1_BASE_TEXTURE2 == 1)
    vec4 baseColor2 = SampleSource1SurfaceTexture(g_tColor2, vTexCoordOut);

    baseColor.rgb = mix(baseColor.rgb, baseColor2.rgb, blend);
    blendedAlpha = mix(baseAlpha, baseColor2.a, blend);
#endif

#if (SOURCE1_FOUR_WAY_BLEND == 1)
    vec4 baseColor2 = SampleSource1SurfaceTexture(g_tColor2, vTexCoordOut, g_vUvScale2);
    vec4 baseColor3 = SampleSource1SurfaceTexture(g_tColor3, vTexCoordOut, g_vUvScale3);
    vec4 baseColor4 = SampleSource1SurfaceTexture(g_tColor4, vTexCoordOut, g_vUvScale4);

    vec3 layerBlends = GetSource1LayerBlends(baseColor.rgb, baseColor2.rgb, baseColor3.rgb, baseColor4.rgb);
    baseColor = BlendSource1Layers(baseColor, baseColor2, baseColor3, baseColor4, layerBlends);

    // The blended alpha is the opacity, the self illumination and the environment map mask
    baseAlpha = baseColor.a;
    blendedAlpha = baseAlpha;
#endif

    vec4 detailColor = SampleSource1Detail(g_tDetail, vTexCoordOut);

#if (SOURCE1_WORLD_TWO_TEXTURE_BLEND == 1)
    vec3 albedo = BlendSource1WorldDetail(baseColor.rgb, detailColor) * vVertexColorOut.rgb;
#elif (SOURCE1_FOUR_WAY_BLEND == 1)
    vec4 albedoColor = baseColor;

    if (F_DETAIL != 0)
    {
        albedoColor = Source1TextureCombine(baseColor, detailColor, g_nDetailBlendMode, GetSource1LayerDetailBlendFactor(layerBlends));
    }

    vec3 albedo = albedoColor.rgb * vVertexColorOut.rgb;
#else
    vec3 albedo = ApplySource1Detail(baseColor, detailColor).rgb * vVertexColorOut.rgb;
#endif

    vec4 normalTexel = vec4(127.0 / 255.0, 127.0 / 255.0, 1.0, 1.0);
    vec3 tangentNormal = vec3(0.0, 0.0, 1.0);
    float bumpMaskAlpha = 1.0;

    if (F_BUMPMAP != 0)
    {
        normalTexel = SampleSource1SurfaceTexture(g_tNormal, vBumpTexCoordOut);
        tangentNormal = DecodeSource1BumpTexel(normalTexel.xyz);

        if (F_BASE_TEXTURE_NO_ENVMAP != 0)
        {
            normalTexel.a = 0.0;
        }

#if (SOURCE1_FOUR_WAY_BLEND == 1)
        tangentNormal = BlendSource1LayerNormals(tangentNormal, layerBlends);
#elif (SOURCE1_BUMPMAP2 == 1)
        vec4 normalTexel2 = texture(g_tNormal2, vBumpTexCoord2Out);

        if (F_BASE_TEXTURE2_NO_ENVMAP != 0)
        {
            normalTexel2.a = 0.0;
        }

        vec3 tangentNormal2 = DecodeSource1BumpTexel(normalTexel2.xyz);

#if (SOURCE1_BUMPMASK == 1)
        // The mask holds still on the base texture coordinates. Its alpha keeps the sum of the two normal maps, and
        // elsewhere its own normal shows.
        vec4 bumpMaskTexel = texture(g_tBumpMask, vTexCoordOut);
        bumpMaskAlpha = bumpMaskTexel.a;
        tangentNormal = mix(DecodeSource1BumpTexel(bumpMaskTexel.xyz), normalize(tangentNormal + tangentNormal2), bumpMaskAlpha);
#else
        tangentNormal = mix(tangentNormal, tangentNormal2, blend);
#endif

        // Only a blend modulate texture blends the alpha of the normal maps too
        if (F_BLEND_MODULATE != 0)
        {
            normalTexel.a = mix(normalTexel.a, normalTexel2.a, blend);
        }
#endif
    }

#if (S_LIGHTMAP_VERSION_MINOR == 0)
    // A material shown outside of a map has no lightmap and takes full light
    vec4 lightmapSample = vec4(1.0, 1.0, 1.0, 0.0);
#else
    vec4 lightmapSample;

    if (F_BUMPMAP != 0 && F_BUMPED_LIGHTMAP != 0)
    {
        lightmapSample = GetSource1BumpedLightmap(vLightmapUVOut, tangentNormal, detailColor);
    }
    else
    {
        lightmapSample = SampleSource1Lightmap(g_tLightmap, vLightmapUVOut);
    }
#endif

    vec3 geometricNormal = GetSource1SurfaceNormal(vNormalOut, vFragPosition);
    vec3 lightmap = ApplySource1SunShadow(lightmapSample, vFragPosition);
    lightmap += GetSource1DynamicLightmap(vFragPosition, geometricNormal, tangentNormal, detailColor);
    lightmap *= g_vColorTint * g_flLightmapScale;

    if (F_LIGHTWARP != 0 && F_DETAIL == 0 && g_flSeamlessScale == 0.0)
    {
        float warpCoord = 0.5 * length(lightmap);
        lightmap *= 2.0 * texture(g_tLightWarp, saturate(vec2(warpCoord, 0.0))).rgb;
    }

    vec3 color = albedo * lightmap;

    color = ApplySource1SelfIllum(color, albedo, baseAlpha, g_tSelfIllumMask, vTexCoordOut);

    // Only the additive detail blend mode adds to the lit color, mode 6 has no lightmapped variant
    if (F_DETAIL != 0 && g_nDetailBlendMode == 5)
    {
        color += g_flDetailBlendFactor * detailColor.rgb;
    }

    vec3 normal = geometricNormal;
    vec3 eyeDirection = normalize(g_vCameraPositionWs - vFragPosition);
    vec3 specularLight = vec3(0.0);

    if (F_BUMPMAP != 0)
    {
        normal = GetSource1BumpedNormal(geometricNormal, tangentNormal);
    }

    // Projected textures light the surface from their direction
    for (uint lightIndex = g_nNumLights[0]; lightIndex < g_nNumLights[1]; lightIndex++)
    {
        if (IsSource1DynamicLight(lightIndex))
        {
            continue;
        }

        vec3 lightDirection;
        vec3 lightColor = GetSource1LightColor(lightIndex, vFragPosition, lightDirection);

        color += albedo * lightColor * max(dot(normal, lightDirection), 0.0);
    }

    if (F_ENVMAP != 0)
    {
        float fresnel = pow(max(1.0 - dot(normal, eyeDirection), 0.0), SOURCE1_FRESNEL_POWER);
        fresnel = fresnel * (1.0 - g_flFresnelReflection) + g_flFresnelReflection;

        vec3 envmapMask = GetSource1EnvmapMask(vEnvMapMaskTexCoordOut, blendedAlpha, normalTexel.a) * bumpMaskAlpha;
        vec3 envmapLight = saturate((lightmap - g_vEnvMapLightScaleRange.x) * g_vEnvMapLightScaleRange.y);
        envmapMask *= mix(vec3(1.0), envmapLight, g_flEnvMapLightScale);
        specularLight = GetSource1EnvmapColor(normal, vFragPosition, envmapMask, g_vEnvMapTint) * fresnel;
        color += specularLight;
    }

    ApplyFog(color, vFragPosition);

    float alpha = GetSource1Opacity(baseAlpha, vVertexColorOut.a);
    outputColor = vec4(color, ApplySource1AlphaTest(alpha));

    if (g_iRenderMode != 0)
    {
        vec3 tangent = GetSource1TangentFrame(geometricNormal, vTangentOut, vFragPosition, vBumpTexCoordOut)[0];

        ApplySource1RenderModes(outputColor, albedo, vVertexColorOut, normal, eyeDirection, g_tColor, vTexCoordOut);
        ApplySource1NormalRenderModes(outputColor, geometricNormal, tangent, tangentNormal, normal);
        ApplySource1LightingRenderModes(outputColor, lightmap, specularLight);
        ApplySource1CubemapRenderMode(outputColor, normal, vFragPosition);
    }
}
