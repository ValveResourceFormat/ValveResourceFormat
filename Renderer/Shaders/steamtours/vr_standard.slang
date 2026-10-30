#version 460

// The entry file includes the features file of its game first.

#ifndef AMBIENT_OCCLUSION_SPECULAR_ONLY
#define AMBIENT_OCCLUSION_SPECULAR_ONLY 0 // The occlusion of the indirect light texture darkens the specular light alone
#endif

#ifndef REFLECTANCE_FROM_SECOND_TEXTURE
#define REFLECTANCE_FROM_SECOND_TEXTURE 1 // Translucent and alpha tested materials read their reflectance from g_tMetalnessReflectance2
#endif

#if (F_INDIRECT_TEXTURES > 0 && F_INDIRECT_TEXTURES_UV2 == 1)
    #define F_SECONDARY_UV 1
#endif

#define IBL (F_SPECULAR_CUBE_MAP == 1)
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0
#define GLASS_BACKLIGHT (F_TRANSMISSIVE_SUNLIGHT == 1)

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"
#include "../features/blend_second_layer.slang"

uniform sampler2D g_tNormal;
uniform sampler2D g_tTintMask;

#if (F_TRANSMISSIVE_THICKNESS == 1)
    uniform sampler2D g_tTransmissiveThickness;
#endif

#if (F_HIGH_QUALITY_GLOSS == 1)
    uniform sampler2D g_tNormal2;
    uniform sampler2D g_tGloss;
#endif

#if (TRANSLUCENT || (F_ALPHA_TEST == 1))
    uniform sampler2D g_tColor; // SrgbRead(true) RepresentativeTexture
#elif (F_SPECULAR == 1 && F_METALNESS_TEXTURE == 0)
    uniform sampler2D g_tColor1; // SrgbRead(true)
#else
    uniform sampler2D g_tColor2; // SrgbRead(true)
#endif

uniform float g_flOpacityScale = 1.0;
uniform float g_flAlphaTestReference = 0.5;

#if (F_GLASS == 1)
    uniform float g_flGlassFresnelExponent = 1.0;
    uniform float g_flGlassFresnelThickness = 0.0;
    uniform float g_flGlassFresnelMaxOpacity = 0.0;
#endif

#if (F_BLEND > 0)
    in vec2 vColorBlendValues;
    in vec4 vLayerTexCoords;

    #if (F_SPECULAR == 1)
        uniform sampler2D g_tLayer1Color; // SrgbRead(true)
    #else
        uniform sampler2D g_tLayer1Color2; // SrgbRead(true)
    #endif

    uniform sampler2D g_tLayer1RevealMask;
    uniform float g_flLayer1BlendSoftness = 0.5;
    uniform vec3 g_vLayer1BorderColor = vec3(1.0);
    uniform float g_flLayer1BorderSoftness = 0.5;
    uniform float g_flLayer1BorderOffset = 0.0;
    uniform float g_flLayer1BorderStrength = 0.5;

    #if (F_BLEND_NORMALS == 1)
        uniform sampler2D g_tLayer1Normal;
    #endif
#endif

#if (F_BLEND == 2)
    #if (F_SPECULAR == 1)
        uniform sampler2D g_tLayer2Color; // SrgbRead(true)
    #else
        uniform sampler2D g_tLayer2Color2; // SrgbRead(true)
    #endif

    uniform sampler2D g_tLayer2RevealMask;
    uniform float g_flLayer2BlendSoftness = 0.5;
    uniform vec3 g_vLayer2BorderColor = vec3(1.0);
    uniform float g_flLayer2BorderSoftness = 0.5;
    uniform float g_flLayer2BorderOffset = 0.0;
    uniform float g_flLayer2BorderStrength = 0.5;

    #if (F_BLEND_NORMALS == 1)
        uniform sampler2D g_tLayer2Normal;
    #endif
#endif

#if (F_METALNESS_TEXTURE == 1)
    uniform sampler2D g_tMetalnessReflectance;
    uniform vec2 g_vMetalnessRange = vec2(0.0, 1.0);
#else
    uniform float g_flMetalness = 0.0;

    #if (F_SPECULAR == 1 && (TRANSLUCENT || (F_ALPHA_TEST == 1)))
        #if (REFLECTANCE_FROM_SECOND_TEXTURE)
            uniform sampler2D g_tMetalnessReflectance2;
        #else
            uniform sampler2D g_tMetalnessReflectance;
        #endif
    #endif
#endif

uniform vec2 g_vReflectanceRange = vec2(0.0, 1.0);

#if (F_ADVANCED_FRESNEL_CONTROLS == 1)
    uniform float g_flFresnelFacing = 1.0;
    uniform vec2 g_vFresnelFacingRange = vec2(0.0);
    uniform float g_flFresnelExponent = 5.0;
#endif

#if (F_SPECULAR_CUBE_MAP == 1)
    uniform float g_flCubeMapScalar = 1.0;

    #if (ENV_SPECULAR_SCHLICK)
        uniform float g_flCubeMapBlurAmount = 1.0;
    #else
        uniform float g_flCubeMapBrightness = 1.0;
        uniform float g_flCubeMapBlur = 0.0;
    #endif
#endif

#if (F_INDIRECT_TEXTURES == 1)
    uniform sampler2D g_tIndirectLight;
    uniform float g_flIndirectScale = 1.0;
#endif

#if (F_INDIRECT_TEXTURES == 2)
    uniform sampler2D g_tAmbientOcclusion;
    uniform float g_flAmbientOcclusionStrengthDirectDiffuse = 1.0;
    uniform float g_flAmbientOcclusionStrengthDirectSpecular = 1.0;
#endif

#include "vr_surface_lighting_extras.slang"

vec4 SampleColor(vec2 texCoord)
{
#if (TRANSLUCENT || (F_ALPHA_TEST == 1))
    return texture(g_tColor, texCoord);
#elif (F_SPECULAR == 1 && F_METALNESS_TEXTURE == 0)
    return texture(g_tColor1, texCoord);
#else
    return texture(g_tColor2, texCoord);
#endif
}

#if (F_BLEND > 0)
// The share of a layer: a linear ramp over the paint, where the reveal mask sets how much paint it takes to show
float GetLayerBlend(float paint, float revealMask, float softness)
{
    float ramp = softness * 2.0;
    return saturate((paint * (1.0 + ramp) - revealMask) / ramp);
}

// The border color of a layer, strongest along the middle of the ramp of its blend
vec3 GetLayerBorder(float paint, float revealMask, vec3 borderColor, float softness, float offset, float strength)
{
    float ramp = softness * 2.0;
    float edge = saturate((saturate(paint - offset) * (1.0 + ramp) - revealMask) / ramp);
    float band = 1.0 - abs(edge * 2.0 - 1.0);
    return mix(vec3(1.0), borderColor, band * strength);
}

// Mixes a layer over the color and the normal texture by its vertex paint, with the samples of the layer
void BlendLayer(inout vec4 color, inout vec4 normalTexture, vec2 texCoord, vec4 layerColor, vec4 layerNormalTexture,
    float paint, float revealMask, float softness, vec3 borderColor, float borderSoftness, float borderOffset, float borderStrength)
{
    Layer2_t layer;
    layer.TexCoord = texCoord;
    layer.Color = layerColor;
    layer.NormalTexture = layerNormalTexture;

    BlendLayer2(color, normalTexture, layer, GetLayerBlend(paint, revealMask, softness));
    color.rgb *= GetLayerBorder(paint, revealMask, borderColor, borderSoftness, borderOffset, borderStrength);
}
#endif

// Sets the metalness and returns the reflectance, which is the color texture's alpha unless the opacity needs it
float InitMetalnessReflectance(inout MaterialProperties_t mat, vec2 texCoord, float colorAlpha)
{
#if (F_METALNESS_TEXTURE == 1)
    vec2 metalnessReflectance = texture(g_tMetalnessReflectance, texCoord).rg;
    mat.Metalness = RemapByRange(metalnessReflectance.r, g_vMetalnessRange);
    return metalnessReflectance.g;
#else
    mat.Metalness = g_flMetalness;

    #if (F_SPECULAR == 1 && (TRANSLUCENT || (F_ALPHA_TEST == 1)))
        #if (REFLECTANCE_FROM_SECOND_TEXTURE)
            return texture(g_tMetalnessReflectance2, texCoord).g;
        #else
            return texture(g_tMetalnessReflectance, texCoord).g;
        #endif
    #else
        return colorAlpha;
    #endif
#endif
}

void InitAlbedoAndOpacity(inout MaterialProperties_t mat, vec2 texCoord, vec4 color)
{
    mat.Albedo = color.rgb;

#if (TRANSLUCENT || (F_ALPHA_TEST == 1))
    mat.Opacity = color.a;
#endif

#if (TRANSLUCENT)
    mat.Opacity *= g_flOpacityScale;
#endif

#if (F_ALPHA_TEST == 1)
    mat.Opacity = AlphaTestAntiAliasing(mat.Opacity * vVertexColorOut.a, texCoord, g_flAlphaTestReference);
#endif

#if (F_TINT_MASK == 1)
    // The mask is painted in the green channel
    mat.Albedo *= GetMaskedVertexTint(texture(g_tTintMask, texCoord).y);
#else
    mat.Albedo *= vVertexColorOut.rgb;
#endif
}

// The high quality gloss shader reads its normal from a texture of its own and adds a gloss texture to the roughness
void InitNormalAndRoughness(inout MaterialProperties_t mat, vec2 texCoord, vec4 normalTexture)
{
#if (F_HIGH_QUALITY_GLOSS == 1)
    normalTexture = texture(g_tNormal2, texCoord);
#endif

    mat.NormalMap = DecodeHemiOctahedronNormal(normalTexture.ag);
    mat.RoughnessTex.xy = normalTexture.rb;

#if (F_HIGH_QUALITY_GLOSS == 1)
    mat.RoughnessTex.xy = min(mat.RoughnessTex.xy + texture(g_tGloss, texCoord).ag, vec2(1.0));
#endif

    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);
}

float GetReflectance(inout MaterialProperties_t mat, vec2 texCoord, float colorAlpha)
{
    float reflectance = InitMetalnessReflectance(mat, texCoord, colorAlpha);

#if (F_SPECULAR == 1)
    return RemapByRange(reflectance, g_vReflectanceRange);
#else
    return 0.0;
#endif
}

#if (F_INDIRECT_TEXTURES > 0)
// The occlusion darkens the diffuse light as it is, and the specular light less where the surface faces the eye.
// Where the occlusion is for the specular light alone, it darkens that light as it is.
void SetAmbientOcclusion(inout MaterialProperties_t mat, float occlusion)
{
    mat.AmbientOcclusion = occlusion;

#if (AMBIENT_OCCLUSION_SPECULAR_ONLY)
    mat.DiffuseAO = vec3(1.0);
    mat.SpecularAO = occlusion;
#else
    float facing = saturate(dot(mat.ViewDir, mat.Normal));

    mat.DiffuseAO = vec3(occlusion);
    mat.SpecularAO = mix(pow(occlusion, 8.0), 1.0, saturate(facing * facing - 0.3));
#endif
}
#endif

#if (F_INDIRECT_TEXTURES == 1)
// The painted light is the diffuse indirect light, and its alpha is the occlusion
void ApplyIndirectLightTexture(inout MaterialProperties_t mat, vec2 texCoord)
{
    vec4 indirectLight = texture(g_tIndirectLight, GetUvSet(texCoord, F_INDIRECT_TEXTURES_UV2 == 1));

    mat.IndirectLightTexture = indirectLight.rgb * g_flIndirectScale;

    SetAmbientOcclusion(mat, indirectLight.a);
}
#endif

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    vec4 color = SampleColor(texCoord);
    vec4 normalTexture = texture(g_tNormal, texCoord);

#if (F_BLEND > 0)
    // The first layer is painted by the first channel of the vertex paint
    vec2 layer1TexCoord = vLayerTexCoords.xy;
    vec4 layer1Normal = normalTexture;

    #if (F_SPECULAR == 1)
        vec4 layer1Color = texture(g_tLayer1Color, layer1TexCoord);
    #else
        vec4 layer1Color = texture(g_tLayer1Color2, layer1TexCoord);
    #endif

    #if (F_BLEND_NORMALS == 1)
        layer1Normal = texture(g_tLayer1Normal, layer1TexCoord);
    #endif

    BlendLayer(color, normalTexture, layer1TexCoord, layer1Color, layer1Normal,
        vColorBlendValues.x, texture(g_tLayer1RevealMask, layer1TexCoord).g, g_flLayer1BlendSoftness,
        g_vLayer1BorderColor, g_flLayer1BorderSoftness, g_flLayer1BorderOffset, g_flLayer1BorderStrength);
#endif
#if (F_BLEND == 2)
    // The second layer goes over the first one, by the second channel
    vec2 layer2TexCoord = vLayerTexCoords.zw;
    vec4 layer2Normal = normalTexture;

    #if (F_SPECULAR == 1)
        vec4 layer2Color = texture(g_tLayer2Color, layer2TexCoord);
    #else
        vec4 layer2Color = texture(g_tLayer2Color2, layer2TexCoord);
    #endif

    #if (F_BLEND_NORMALS == 1)
        layer2Normal = texture(g_tLayer2Normal, layer2TexCoord);
    #endif

    BlendLayer(color, normalTexture, layer2TexCoord, layer2Color, layer2Normal,
        vColorBlendValues.y, texture(g_tLayer2RevealMask, layer2TexCoord).g, g_flLayer2BlendSoftness,
        g_vLayer2BorderColor, g_flLayer2BorderSoftness, g_flLayer2BorderOffset, g_flLayer2BorderStrength);
#endif

    InitAlbedoAndOpacity(mat, texCoord, color);

#if (F_UNLIT == 1)
    // Unlit, the glow replaces the color instead of adding to it
    #if (F_SELF_ILLUM == 1)
        mat.Albedo *= GetSelfIllumScale(texCoord);
    #endif

    FinishUnlitMaterial(mat);
    return mat;
#endif

    InitNormalAndRoughness(mat, texCoord, normalTexture);

#if (F_GLASS == 1)
    // The edges of the glass are more opaque
    float glassFacing = saturate(dot(mat.ViewDir, mat.Normal) - g_flGlassFresnelThickness);
    mat.Opacity += pow(1.0 - glassFacing, g_flGlassFresnelExponent) * g_flGlassFresnelMaxOpacity;
#endif

    float reflectance = GetReflectance(mat, texCoord, color.a);

#if (F_ADVANCED_FRESNEL_CONTROLS == 1)
    // The facing weight takes the reflectance from the low end of the range to the high end
    float fresnelFacingScale = max(g_vFresnelFacingRange.x, g_vFresnelFacingRange.y) - g_vFresnelFacingRange.x;
    reflectance *= g_flFresnelFacing * fresnelFacingScale + g_vFresnelFacingRange.x;
    mat.FresnelExponent = g_flFresnelExponent;
#endif

    AdjustRoughnessByGeometricNormal(mat);
    ResolveAmbientNormal(mat, texCoord);

#if (F_SPECULAR_CUBE_MAP == 1)
    #if (ENV_SPECULAR_SCHLICK)
        mat.EnvironmentBlur = g_flCubeMapBlurAmount * 9.0 + 1.0;
    #else
        mat.EnvironmentBlur = (g_flCubeMapBlur + 1.0) * 0.7;
    #endif
#endif

    mat.Reflectance = reflectance;

    // Metalness only tints the specular
    mat.DiffuseColor = mat.Albedo - mat.Albedo * reflectance;
    mat.SpecularColor = reflectance * mix(vec3(1.0), mat.Albedo, mat.Metalness);

#if (F_TRANSMISSIVE_SUNLIGHT == 1)
    // Without a thickness texture the surface is as thin as it gets, and only the light passing through it shows
    #if (F_TRANSMISSIVE_THICKNESS == 1)
        float thickness = texture(g_tTransmissiveThickness, texCoord).y;
    #else
        float thickness = 0.0;
    #endif
    mat.DirectDiffuseScale = thickness;
    mat.TransmissiveColor = mat.DiffuseColor * (1.0 - thickness);
#endif

    // The glow is added to the diffuse lighting, so it takes the diffuse color
    mat.IllumColor = GetSelfIllumScale(texCoord) * mat.DiffuseColor;

    FinishMaterial(mat);

#if (F_INDIRECT_TEXTURES == 1)
    ApplyIndirectLightTexture(mat, texCoord);
#elif (F_INDIRECT_TEXTURES == 2)
    SetAmbientOcclusion(mat, texture(g_tAmbientOcclusion, GetUvSet(texCoord, F_INDIRECT_TEXTURES_UV2 == 1)).x);
#endif

    return mat;
}

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.DirectSpecular = F_SPECULAR == 1;
    options.PremultipliedOpacity = F_GLASS == 1;

#if (F_SPECULAR == 0)
    options.SpecularIndirectScale = 0.0;
#elif (F_SPECULAR_CUBE_MAP == 1)
    #if (ENV_SPECULAR_SCHLICK)
        options.SpecularIndirectScale = g_flCubeMapScalar;
    #else
        options.SpecularIndirectScale = g_flCubeMapBrightness * g_flCubeMapScalar * 1.1;
    #endif
#endif
#if (F_WORLD_LIGHTING_GRADIENT == 1)
    options.IndirectLightScale = GetWorldLightingGradient(vFragPosition);
#endif
#if (F_INDIRECT_TEXTURES == 2)
    options.DirectDiffuseAO = g_flAmbientOcclusionStrengthDirectDiffuse;
    options.DirectSpecularAO = g_flAmbientOcclusionStrengthDirectSpecular;
#endif
#if (F_BLEND > 0)
    options.BlendValues = vec3(vColorBlendValues, 0.0);
#endif

#if (F_UNLIT == 1)
    RenderUnlitSurface(g_tNormal, options);
#else
    RenderSurface(g_tNormal, options);
#endif
}
