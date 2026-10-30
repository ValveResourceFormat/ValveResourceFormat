#version 460

#include "common_ps.slang"
#include "common_envmap_ps.slang"

in vec3 vFragPosition;
in vec3 vNormalOut;
in vec4 vTangentOut;
in vec2 vTexCoordOut;
in vec2 vLightmapUVOut;
in vec4 vVertexColorOut;
in float vBlendOut;

uniform sampler2D g_tColor; // SrgbRead(true) RepresentativeTexture
uniform sampler2D g_tColor2; // SrgbRead(true)
uniform sampler2D g_tDetail;
uniform sampler2D g_tSelfIllumMask;
uniform sampler2D g_tBlendModulate;
uniform sampler2D g_tNormal;
uniform sampler2D g_tNormal2;
uniform sampler2D g_tLightWarp;
uniform sampler2D g_tLightmap;
uniform sampler2D g_tLightmap1;
uniform sampler2D g_tLightmap2;
uniform sampler2D g_tLightmap3;

uniform int F_BLEND_MODULATE;
uniform int F_BUMPMAP;
uniform int F_SSBUMP;
uniform int F_BUMPED_LIGHTMAP;
uniform int F_LIGHTWARP;
// The tint multiplies the light, in the color space it was written in
uniform vec3 g_vColorTint = vec3(1.0);
uniform float g_flLightmapScale = 1.0;

// The light on a bump mapped surface, from the three bumped lightmaps. tangentNormal is the normal of the bump map in
// tangent space, or for a self shadowed bump map the weights of the three lightmaps.
vec3 GetSource1BumpedLightmap(vec2 lightmapCoord, vec3 tangentNormal)
{
    vec3 light1 = texture(g_tLightmap1, lightmapCoord).rgb;
    vec3 light2 = texture(g_tLightmap2, lightmapCoord).rgb;
    vec3 light3 = texture(g_tLightmap3, lightmapCoord).rgb;

    if (F_SSBUMP != 0)
    {
        return tangentNormal.x * light1 + tangentNormal.y * light2 + tangentNormal.z * light3;
    }

    vec3 weights;
    weights.x = saturate(dot(tangentNormal, SOURCE1_BUMP_BASIS[0]));
    weights.y = saturate(dot(tangentNormal, SOURCE1_BUMP_BASIS[1]));
    weights.z = saturate(dot(tangentNormal, SOURCE1_BUMP_BASIS[2]));
    weights *= weights;

    float sum = max(weights.x + weights.y + weights.z, 0.0001);

    return (weights.x * light1 + weights.y * light2 + weights.z * light3) / sum;
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

void main()
{
    vec4 baseColor = texture(g_tColor, vTexCoordOut);

    // The opacity and the self illumination use the alpha of the first texture, the environment map mask the blended one
    float baseAlpha = baseColor.a;
    float blendedAlpha = baseAlpha;
    float blend = 0.0;

#if (SOURCE1_BLEND_BY_VERTEX_ALPHA == 1)
    blend = vBlendOut;

    if (F_BLEND_MODULATE != 0)
    {
        vec4 modulate = texture(g_tBlendModulate, vTexCoordOut);
        float low = saturate(modulate.g - modulate.r);
        float high = saturate(modulate.g + modulate.r);
        blend = smoothstep(low, max(high, low + 0.0001), blend);
    }
#endif

#if (SOURCE1_BASE_TEXTURE2 == 1)
    vec4 baseColor2 = texture(g_tColor2, vTexCoordOut);

    baseColor.rgb = mix(baseColor.rgb, baseColor2.rgb, blend);
    blendedAlpha = mix(baseAlpha, baseColor2.a, blend);
#endif

    vec4 detailColor = SampleSource1Detail(g_tDetail, vTexCoordOut);
    vec3 albedo = ApplySource1Detail(baseColor, detailColor).rgb * vVertexColorOut.rgb;

    vec4 normalTexel = vec4(0.5, 0.5, 1.0, 1.0);
    vec3 tangentNormal = vec3(0.0, 0.0, 1.0);

    if (F_BUMPMAP != 0)
    {
        normalTexel = texture(g_tNormal, vTexCoordOut);
        tangentNormal = DecodeSource1BumpTexel(normalTexel.xyz);

#if (SOURCE1_BUMPMAP2 == 1)
        vec3 tangentNormal2 = DecodeSource1BumpTexel(texture(g_tNormal2, vTexCoordOut).xyz);
        tangentNormal = mix(tangentNormal, tangentNormal2, blend);
#endif
    }

    vec3 lightmap;

    if (F_BUMPMAP != 0 && F_BUMPED_LIGHTMAP != 0)
    {
        lightmap = GetSource1BumpedLightmap(vLightmapUVOut, tangentNormal);
    }
    else
    {
        lightmap = texture(g_tLightmap, vLightmapUVOut).rgb;
    }

    lightmap *= g_vColorTint * g_flLightmapScale;

    if (F_LIGHTWARP != 0 && F_DETAIL == 0)
    {
        float warpCoord = 0.5 * length(lightmap);
        lightmap *= 2.0 * texture(g_tLightWarp, vec2(warpCoord, 0.0)).rgb;
    }

    vec3 color = albedo * lightmap;

    color = ApplySource1SelfIllum(color, albedo, baseAlpha, g_tSelfIllumMask, vTexCoordOut);

    if (F_ENVMAP != 0)
    {
        vec3 normal = GetSource1SurfaceNormal(vNormalOut, vFragPosition);

        if (F_BUMPMAP != 0)
        {
            vec3 reflectionNormal = tangentNormal;

            if (F_SSBUMP != 0)
            {
                reflectionNormal = normalize(SOURCE1_BUMP_BASIS[0] * tangentNormal.x
                    + SOURCE1_BUMP_BASIS[1] * tangentNormal.y
                    + SOURCE1_BUMP_BASIS[2] * tangentNormal.z);
            }

            mat3 tangentFrame = GetSource1TangentFrame(normal, vTangentOut, vFragPosition, vTexCoordOut);
            normal = normalize(tangentFrame * reflectionNormal);
        }

        vec3 eyeDirection = normalize(g_vCameraPositionWs - vFragPosition);
        float fresnel = pow(max(1.0 - dot(normal, eyeDirection), 0.0), 5.0);
        fresnel = fresnel * (1.0 - g_flFresnelReflection) + g_flFresnelReflection;

        vec3 envmapMask = GetSource1EnvmapMask(vTexCoordOut, blendedAlpha, normalTexel.a);
        color += GetSource1EnvmapColor(normal, vFragPosition, envmapMask, g_vEnvMapTint) * fresnel;
    }

    ApplyFog(color, vFragPosition);

    float alpha = GetSource1Opacity(baseAlpha, vVertexColorOut.a);
    outputColor = vec4(color, ApplySource1AlphaTest(alpha));
}
