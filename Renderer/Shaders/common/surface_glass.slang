#version 460

#ifndef VRF_SURFACE_GLASS_INCLUDE
#define VRF_SURFACE_GLASS_INCLUDE

// Steps of the glass shaders that compose the lit glass over the light it lets through.
// Include it after surface_shading.slang.

// How far the surface faces away from the view, offset by the thickness of the edge
float GetGlassEdge(MaterialProperties_t mat, float thickness, float falloff)
{
    float viewDotNormal = dot(mat.ViewDir, mat.Normal);
    return pow(saturate(1.0 - (viewDotNormal - thickness)), falloff);
}

#if (S_MODE_DEPTH == 0)

// The scene behind the glass, offset by the normal map and optionally blurred
vec3 SampleRefraction(sampler2D sceneColor, MaterialProperties_t mat, float refractScale, bool blur)
{
    // Screen space is y up here, the normal map offsets towards the bottom of the screen for positive y
    vec2 offset = vec2(mat.NormalMap.x, -mat.NormalMap.y) * (refractScale * vVertexColorOut.a);
    vec2 uv = gl_FragCoord.xy / vec2(textureSize(sceneColor, 0)) + offset;

    if (!blur)
    {
        return texture(sceneColor, uv).rgb;
    }

    const float texel = 1.0 / 1024.0;
    vec3 refracted = texture(sceneColor, uv - vec2(texel)).rgb * (4.0 / 9.0);
    refracted += texture(sceneColor, uv + vec2(2.0 * texel, -texel)).rgb * (2.0 / 9.0);
    refracted += texture(sceneColor, uv + vec2(-texel, 2.0 * texel)).rgb * (2.0 / 9.0);
    refracted += texture(sceneColor, uv + vec2(2.0 * texel)).rgb * (1.0 / 9.0);
    return refracted;
}

// The fog covers what is behind the glass as much as the glass itself.
// Returns the fogged color, and attenuates the transmission.
vec3 FogGlass(vec3 color, inout vec3 transmission, vec3 positionWs)
{
    vec3 foggedColor = color;
    ApplyFog(foggedColor, positionWs);
    transmission *= GetFogAttenuation(positionWs);
    return foggedColor;
}

// Without a second blend source the transmission becomes its luminance, as the alpha the color is blended with
vec4 GetGlassBlendOutput(vec3 color, vec3 transmission)
{
    float alpha = max(1.0 - GetLuma(transmission), 1.0 / 255.0);
    return vec4(color / alpha, alpha);
}

// The lit glass added to what is behind it filtered by the transmission, with the fog and the glass blend of the shader.
// The transmissive lighting is the direct light that reaches the glass from behind.
vec4 GetGlassOutput(MaterialProperties_t mat, vec3 diffuseLighting, vec3 transmissiveLighting, vec3 specularLighting);

#endif

// main() of a glass shader: the depth and overdraw passes, or the lit glass, tone mapping and render modes
void RenderGlass(sampler2D representativeTexture)
{
#if (S_MODE_DEPTH != 0)
    RenderDepthPass();
#else
    MaterialProperties_t mat = GetShadedMaterial();

    LightingTerms_t lighting;
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.OccludeDirectDiffuse = false;

    vec3 diffuseLighting;
    vec3 specularLighting;
    LightSurface(mat, lighting, options, diffuseLighting, specularLighting);

    outputColor = GetGlassOutput(mat, diffuseLighting, lighting.TransmissiveDirect, specularLighting);
    ApplyToneMappingOverride(outputColor);

    HandleSurfaceRenderModes(outputColor, mat, lighting, representativeTexture);
#endif
}

#endif
