#version 460

#ifndef VRF_SURFACE_MATERIAL_INCLUDE
#define VRF_SURFACE_MATERIAL_INCLUDE

// Steps of GetMaterial() that the lit surface shaders share, see surface_base_ps.slang.
// A shader calls the ones its material needs, in this order, with its own steps in between.

// True for the materials that blend with what is behind them. Only for preprocessor conditions, where a combo the
// shader does not ship counts as 0.
#define TRANSLUCENT ((F_TRANSLUCENT == 1) || (F_GLASS == 1) || ((F_BLEND_MODE > 0) && (F_BLEND_MODE != 2)))

// True for the blend modes that blend with what is behind them, every one but the emissive mode
bool IsBlendModeTranslucent(int blendMode)
{
    return (blendMode > 0) && (blendMode != 2);
}

// Fades an opaque surface out by the vertex alpha with a dither
void DiscardByVertexFade()
{
    if (vVertexColorOut.a <= (254.0/255.0))
    {
        vec4 blueNoiseZeroToOne = texelFetch(g_tBlueNoise, ivec2(mod(gl_FragCoord.xy, textureSize(g_tBlueNoise, 0))), 0);
        if (vVertexColorOut.a <= blueNoiseZeroToOne.x)
        {
            discard;
        }
    }
}

// The coordinates of the secondary UV set when the material asks for it, otherwise the given ones
vec2 GetUvSet(vec2 texCoord, bool useSecondaryUv)
{
#if (F_SECONDARY_UV == 1)
    if (useSecondaryUv)
    {
        return vTexCoord2;
    }
#endif
    return texCoord;
}

// Opacity of the color alpha, scaled by the opacity scale of translucent materials and alpha tested when asked to.
// Opaque materials have no alpha and fade out by the vertex alpha instead.
void InitOpacity(inout MaterialProperties_t mat, float colorAlpha, float opacityScale, bool translucent, bool scaleByVertexAlpha, vec2 texCoord, float alphaTestReference)
{
    bool usesAlpha = translucent;

#if (F_ALPHA_TEST == 1) || (ALPHA_TEST_BY_BLEND_MODE == 1)
    usesAlpha = true;
#endif

    if (usesAlpha)
    {
        mat.Opacity = colorAlpha;

        if (scaleByVertexAlpha)
        {
            mat.Opacity *= vVertexColorOut.a;
        }
    }
    else
    {
        DiscardByVertexFade();
    }

    if (translucent)
    {
        mat.Opacity *= opacityScale;
    }

#if (F_ALPHA_TEST == 1) || (ALPHA_TEST_BY_BLEND_MODE == 1)
    mat.Opacity = AlphaTestAntiAliasing(mat.Opacity, texCoord, alphaTestReference);
#endif
}

// The vertex tint, where the tint mask is 0 the surface is not tinted
vec3 GetMaskedVertexTint(float tintMask)
{
    return 1.0 - tintMask * (1.0 - vVertexColorOut.rgb);
}

vec3 GetSelfIllumination(vec3 illumMask, vec3 albedo, float albedoFactor, float brightness, float scale, vec3 tint)
{
    vec3 selfIllumScale = (exp2(brightness) * scale) * tint;
    return selfIllumScale * illumMask * mix(vec3(1.0), albedo, albedoFactor);
}

// The coordinates scrolled over time. The time snaps to a step of the quantize interval, so animated textures can be
// made to tick rather than slide. A zero interval means no stepping.
vec2 ScrollTexCoord(vec2 texCoord, vec2 speed, float quantize)
{
    return texCoord + fract(speed * GetQuantizedTime(quantize));
}

// The self illumination mask, scrolled over time
vec3 SampleSelfIllumMask(sampler2D illumMask, vec2 texCoord, vec2 scrollSpeed)
{
    return texture(illumMask, ScrollTexCoord(texCoord, scrollSpeed, 0.0)).rgb;
}

// Blends the normal map towards flat, 1.0 leaves it unchanged
vec3 BlendNormalMapTowardsFlat(vec3 normalMap, float amount)
{
    return normalize(mix(vec3(0, 0, 1), normalMap, amount));
}

// Scales the slope of the normal map
vec3 ScaleNormalMapSlope(vec3 normalMap, float scale)
{
    return normalize(vec3(normalMap.xy * scale, normalMap.z));
}

// The normal map blended towards flat when F_SCALE_NORMAL_MAP is on, otherwise with its slope scaled by the bump strength
vec3 ApplyNormalMapStrength(vec3 normalMap, float bumpStrength)
{
    if (F_SCALE_NORMAL_MAP == 1)
    {
        return BlendNormalMapTowardsFlat(normalMap, g_flNormalMapScaleFactor);
    }

    return ScaleNormalMapSlope(normalMap, bumpStrength);
}

// The transmissive color of a material that shines through from behind, the albedo or the transmissive texture
void ApplyTransmissiveColor(inout MaterialProperties_t mat, vec2 texCoord)
{
#if (F_TRANSMISSIVE_BACKFACE_NDOTL == 1)
    vec3 transmissiveColor = texture(g_tTransmissiveColor, texCoord).rgb;
    mat.TransmissiveColor = (F_USE_ALBEDO_FOR_TRANSMISSIVE == 1) ? mat.Albedo : transmissiveColor;
#endif
}

void ResolveAmbientNormal(inout MaterialProperties_t mat, vec2 texCoord)
{
#if (F_USE_BENT_NORMALS == 1)
    GetBentNormal(mat, texCoord);
#else
    mat.AmbientNormal = mat.Normal;
    mat.AmbientGeometricNormal = mat.GeometricNormal;
#endif
}

void ResolveDiffuseSpecular(inout MaterialProperties_t mat, vec3 F0)
{
    mat.DiffuseColor = mat.Albedo - mat.Albedo * mat.Metalness;
    mat.SpecularColor = mix(F0, mat.Albedo, mat.Metalness);
}

#if ((F_ANISOTROPIC_GLOSS == 1) || (F_HIGH_QUALITY_GLOSS == 1))

#if (F_SPHERICAL_PROJECTED_ANISOTROPIC_TANGENTS == 1)
in vec3 vAnisoBitangentOut;
#endif

void CalculateAnisotropicTangents(inout MaterialProperties_t mat)
{
#if (F_SPHERICAL_PROJECTED_ANISOTROPIC_TANGENTS == 1)
    mat.AnisotropicTangent = normalize(cross(vAnisoBitangentOut, mat.Normal));
    mat.AnisotropicBitangent = normalize(cross(mat.Normal, mat.AnisotropicTangent));
#else
    // Recalculate Tangent/Bitangent based on normal map
    mat.AnisotropicTangent = normalize(cross(mat.Bitangent, mat.Normal));
    mat.AnisotropicBitangent = normalize(cross(mat.Normal, mat.Tangent));
#endif
}

#endif

void FinishMaterial(inout MaterialProperties_t mat)
{
    mat.DiffuseAO = vec3(mat.AmbientOcclusion);
    mat.SpecularAO = mat.AmbientOcclusion;

#if ((F_ANISOTROPIC_GLOSS == 1) || (F_HIGH_QUALITY_GLOSS == 1))
    CalculateAnisotropicTangents(mat);
#endif
}

// The steps after the normal, roughness, metalness and ambient occlusion are set, for a material with no steps in between
void FinishLitMaterial(inout MaterialProperties_t mat, vec2 texCoord, vec3 F0)
{
    AdjustRoughnessByGeometricNormal(mat);
    ResolveAmbientNormal(mat, texCoord);
    ResolveDiffuseSpecular(mat, F0);
    FinishMaterial(mat);
}

// Not lit, but shaded by the fullbright view all the same
void FinishUnlitMaterial(inout MaterialProperties_t mat)
{
    mat.Normal = mat.GeometricNormal;
}

#endif
