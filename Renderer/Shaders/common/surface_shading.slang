#version 460

#ifndef VRF_SURFACE_SHADING_INCLUDE
#define VRF_SURFACE_SHADING_INCLUDE

// Lighting, output and render mode steps of main() that the lit surface shaders share, see surface_base_ps.slang.

struct SurfaceOptions_t
{
    int BlendMode;
    bool DirectSpecular;
    bool OccludeDirectDiffuse;
    bool SpecularIgnoresOpacity;
    bool FogMod2x;
    bool FogFadesAlpha;
    bool KeepColorInFullBright;
    float SpecularIndirectScale;
    float DirectDiffuseAO;
    float DirectSpecularAO;
    vec3 BlendValues;
};

// Lit by the direct specular, with the ambient occlusion darkening the direct diffuse light, and no blend mode
SurfaceOptions_t DefaultSurfaceOptions()
{
    SurfaceOptions_t options;
    options.BlendMode = 0;
    options.DirectSpecular = true;
    options.OccludeDirectDiffuse = true;
    options.SpecularIgnoresOpacity = false;
    options.FogMod2x = false;
    options.FogFadesAlpha = false;
    options.KeepColorInFullBright = false;
    options.SpecularIndirectScale = 1.0;
    options.DirectDiffuseAO = 1.0;
    options.DirectSpecularAO = 1.0;
    options.BlendValues = vec3(0.0);
    return options;
}

#if (S_MODE_DEPTH == 0)

// Not lit: the albedo and self illumination as they are
vec4 ShadeUnlit(MaterialProperties_t mat, out LightingTerms_t lighting)
{
    lighting = InitLighting();
    lighting.DiffuseDirect = mat.Albedo + mat.IllumColor;

    return vec4(lighting.DiffuseDirect, mat.Opacity);
}

// Lights the material into the diffuse and specular light that reach the eye.
// Ambient occlusion also darkens the direct diffuse light when OccludeDirectDiffuse is set, the direct AO weights scale its effect on the direct light.
// SpecularIndirectScale scales the indirect specular light after the occlusion.
void LightSurface(inout MaterialProperties_t mat, out LightingTerms_t lighting, SurfaceOptions_t options, out vec3 diffuseLighting, out vec3 specularLighting)
{
    lighting = CalculateLighting(mat);

    if (!options.DirectSpecular)
    {
        lighting.SpecularDirect = vec3(0.0);
    }

    if (options.OccludeDirectDiffuse)
    {
        ApplyAmbientOcclusion(lighting, mat, options.DirectDiffuseAO, options.DirectSpecularAO);
    }
    else
    {
        vec3 unoccludedDiffuseDirect = lighting.DiffuseDirect;
        ApplyAmbientOcclusion(lighting, mat, options.DirectDiffuseAO, options.DirectSpecularAO);
        lighting.DiffuseDirect = unoccludedDiffuseDirect;
    }

    lighting.SpecularIndirect *= options.SpecularIndirectScale;

    diffuseLighting = lighting.DiffuseDirect + lighting.DiffuseIndirect;
    specularLighting = lighting.SpecularDirect + lighting.SpecularIndirect;

    if (F_NO_SPECULAR_AT_FULL_ROUGHNESS == 1)
    {
        specularLighting = (mat.Roughness == vec2(1.0)) ? vec3(0) : specularLighting;
    }
}

// Lights the material and combines the light with its color.
// Translucency fades the diffuse light only, and leaves the surface opaque, when SpecularIgnoresOpacity is set.
vec4 ShadeSurface(inout MaterialProperties_t mat, out LightingTerms_t lighting, SurfaceOptions_t options)
{
    vec3 diffuseLighting;
    vec3 specularLighting;
    LightSurface(mat, lighting, options, diffuseLighting, specularLighting);

    if (options.SpecularIgnoresOpacity)
    {
        vec3 diffuseAndEmission = mat.DiffuseColor * diffuseLighting + lighting.TransmissiveDirect + mat.IllumColor;
        return vec4(specularLighting + diffuseAndEmission * mat.Opacity, 1.0);
    }

    vec3 combinedLighting = mat.DiffuseColor * diffuseLighting + specularLighting + lighting.TransmissiveDirect + mat.IllumColor;
    return vec4(combinedLighting, mat.Opacity);
}

void ApplyToneMappingOverride(inout vec4 color)
{
    if (F_DISABLE_TONE_MAPPING == 1)
    {
        color.rgb = SrgbGammaToLinear(color.rgb);
    }
}

void ApplyFogAndToneMapping(inout vec4 color, vec3 positionWs)
{
    ApplyFog(color.rgb, positionWs);
    ApplyToneMappingOverride(color);
}

// Blend mode 3 fades the color towards gray by its alpha, blend mode 5 towards white and blend mode 6 towards black.
// Blend mode 7 is the modulate of decals, which fades the color in gamma space towards gray.
void ApplyBlendModeOutput(inout vec4 color, int blendMode)
{
    if (blendMode == 3)
    {
        color.rgb = mix(vec3(0.5), color.rgb, vec3(color.a));
    }
    else if (blendMode == 5)
    {
        color.rgb = mix(vec3(1.0), color.rgb, vec3(color.a));
    }
    else if (blendMode == 6)
    {
        color.rgb = mix(vec3(0.0), color.rgb, vec3(color.a));
    }
    else if (blendMode == 7)
    {
        color = vec4(mix(vec3(0.5), SrgbLinearToGamma(color.rgb), vec3(color.a)), color.a);
    }
}

// Writes the view of the active render mode, returns false when it is not one every surface has
bool HandleSurfaceRenderModes(inout vec4 color, MaterialProperties_t mat, LightingTerms_t lighting, sampler2D representativeTexture)
{
    if (HandleMaterialRenderModes(color, mat)
    || HandleLightingRenderModes(color, mat, lighting)
    || HandleUVRenderModes(color, mat, representativeTexture, vTexCoordOut.xy))
    {
        return true;
    }

    if (g_iRenderMode == renderMode_Cubemaps)
    {
        // No bumpmaps, full reflectivity
        vec3 viewmodeEnvMap = GetEnvironment(mat).rgb;
        color.rgb = SrgbLinearToGamma(viewmodeEnvMap);
        return true;
    }

    if (g_iRenderMode == renderMode_Tint)
    {
        color = vVertexColorOut;
        return true;
    }

    return false;
}

#endif

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals);

#if (S_MODE_DEPTH != 0)
// The depth and overdraw passes, which only an alpha tested material writes a color in
void RenderDepthPass()
{
    #if (F_ALPHA_TEST == 1) || (ALPHA_TEST_BY_BLEND_MODE == 1)
        outputColor = vec4(0.0, 0.0, 0.0, GetMaterial(vTexCoordOut, vNormalOut).Opacity);
    #elif (S_MODE_DEPTH == 2)
        outputColor = vec4(0.0, 0.0, 0.0, 1.0);
    #endif

    #if (S_MODE_DEPTH == 2)
        CountQuadOverdraw();
    #endif
}
#else
MaterialProperties_t GetShadedMaterial()
{
    g_bCullFromShadedPosition = g_bFirstpersonLegsDistortion != 0;

    vec3 vertexNormal = SwitchCentroidNormal(vNormalOut, vCentroidNormalOut);
    return GetMaterial(vTexCoordOut, vertexNormal);
}
#endif

// Writes the views of the vertex color and of the vertex painted blend values, returns false when the render mode is neither
bool HandleVertexRenderModes(inout vec4 color, vec3 blendValues)
{
    if (g_iRenderMode == renderMode_VertexColor)
    {
        color.rgb = vVertexColorOut.rgb;
        return true;
    }

    if (g_iRenderMode == renderMode_TerrainBlend)
    {
        color.rgb = blendValues;
        return true;
    }

    return false;
}

#if (S_MODE_DEPTH == 0)
// The fog, tone mapping, blend mode and render modes that follow the shading of outputColor
bool FinishSurface(MaterialProperties_t mat, LightingTerms_t lighting, SurfaceOptions_t options, sampler2D representativeTexture)
{
    if (options.FogMod2x)
    {
        ApplyFogMod2x(outputColor, mat.PositionWS);
    }
    else if (!options.FogFadesAlpha)
    {
        ApplyFog(outputColor.rgb, mat.PositionWS);
    }

    ApplyToneMappingOverride(outputColor);
    ApplyBlendModeOutput(outputColor, options.BlendMode);

    if (options.FogFadesAlpha)
    {
        outputColor.a *= GetFogAttenuation(mat.PositionWS);
    }

    if (options.KeepColorInFullBright && g_iRenderMode == renderMode_FullBright)
    {
        outputColor.rgb = SrgbLinearToGamma(outputColor.rgb);
        return true;
    }

    return HandleSurfaceRenderModes(outputColor, mat, lighting, representativeTexture)
        || HandleVertexRenderModes(outputColor, options.BlendValues);
}
#endif

// main() of a lit surface shader: the depth and overdraw passes, or the shading, fog, blend mode and render modes.
// Returns true when the output is final, which is for the depth passes and when a render mode wrote it.
// The blend modes fade the color by its alpha, see ApplyBlendModeOutput().
// FogMod2x fogs the color of blend mode 3 towards gray, FogFadesAlpha fades the alpha out by the fog after the blend instead of tinting the color,
// and KeepColorInFullBright shows the color as it is written in the fullbright view.
// The vertex color and terrain blend render modes show the vertex color and the BlendValues of vertex painted surfaces.
bool RenderSurface(sampler2D representativeTexture, SurfaceOptions_t options)
{
#if (S_MODE_DEPTH != 0)
    RenderDepthPass();
    return true;
#else
    MaterialProperties_t mat = GetShadedMaterial();

    LightingTerms_t lighting;
    outputColor = ShadeSurface(mat, lighting, options);

    return FinishSurface(mat, lighting, options, representativeTexture);
#endif
}

// RenderSurface() for a surface that is not lit, which shows the albedo and self illumination as they are
bool RenderUnlitSurface(sampler2D representativeTexture, SurfaceOptions_t options)
{
#if (S_MODE_DEPTH != 0)
    RenderDepthPass();
    return true;
#else
    MaterialProperties_t mat = GetShadedMaterial();

    LightingTerms_t lighting;
    outputColor = ShadeUnlit(mat, lighting);

    return FinishSurface(mat, lighting, options, representativeTexture);
#endif
}

#endif
