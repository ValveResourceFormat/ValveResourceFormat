#version 460

// The entry file includes its game's features file first.

// Rect lights shade as area lights
#define BARN_LIGHT_AREA_LIGHTS 1
#define LIGHTMAP_VR_SHADING 1
#define IBL 1
#define SUBSURFACE 1
#define SUBSURFACE_SKIN 1
#define SECOND_SPECULAR_LOBE 1
#define DIFFUSE_AO_COLOR_BLEED

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tNormal; // Sampler(UserConfig)
uniform sampler2D g_tTintMask;
uniform sampler2D g_tCombinedMasks;

uniform vec3 g_vTransmissionColor = vec3(0.75, 0.23, 0.012); // SrgbRead(true)
uniform float g_flMouthInteriorBrightnessScale = 1.0;
uniform float g_flTransmissionFalloff = 0.8;
uniform float g_flDiffuseNormalBlur = 0.5;
uniform float g_flBumpStrength = 1.0;
uniform float g_flOpacityScale = 1.0;
uniform float g_flAmbientOcclusionDirectDiffuse = 1.0;
uniform float g_flAmbientOcclusionDirectSpecular = 1.0;

#if (F_SELF_ILLUM == 1)
    uniform float g_flSelfIllumAlbedoFactor = 1.0;
    uniform float g_flSelfIllumBrightness = 0.0;
    uniform float g_flSelfIllumScale = 1.0;
    uniform vec3 g_vSelfIllumTint = vec3(1.0); // SrgbRead(true)
#endif

uniform float g_flAlphaTestReference = 0.5;

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    // a = transmissive mask
    vec4 color = texture(g_tColor, texCoord);
    vec4 normalTexture = texture(g_tNormal, texCoord);

    // r = mouth mask, g = ambient occlusion, b = self illum mask, a = opacity, tint mask or sss mask
    vec4 combinedMasks = texture(g_tCombinedMasks, texCoord);

    mat.MouthMask = combinedMasks.x;
    mat.AmbientOcclusion = combinedMasks.y;

    mat.SSSMask = (F_SSS_MASK == 1) ? combinedMasks.a : 1.0;

    mat.Albedo = color.rgb;
#if (F_TRANSLUCENT != 0)
    bool translucent = true;
#else
    bool translucent = false;
#endif

    InitOpacity(mat, combinedMasks.a, g_flOpacityScale, translucent, false, texCoord, g_flAlphaTestReference);

#if (F_TINT_MASK == 1)
    mat.Albedo *= GetMaskedVertexTint(texture(g_tTintMask, texCoord).x);
#else
    mat.Albedo *= vVertexColorOut.rgb;
#endif

#if (F_SELF_ILLUM == 1)
    // Membranes fade self illumination by opacity here, the game adds it unfaded
    mat.IllumColor = GetSelfIllumination(vec3(combinedMasks.z), mat.Albedo, g_flSelfIllumAlbedoFactor, g_flSelfIllumBrightness, g_flSelfIllumScale, g_vSelfIllumTint.rgb);
#endif

    mat.NormalMap = DecodeHemiOctahedronNormal(normalTexture.rg);
    mat.RoughnessTex.xy = normalTexture.bb;

    float normalMapAmount = (F_SCALE_NORMAL_MAP == 1) ? g_flNormalMapScaleFactor : g_flBumpStrength;
    mat.NormalMap = BlendNormalMapTowardsFlat(mat.NormalMap, normalMapAmount);

    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

    vec2 vNormalTexels = texCoord * vec2(textureSize(g_tNormal, 0));
    vec2 vNormalDdx = dFdx(vNormalTexels);
    vec2 vNormalDdy = dFdy(vNormalTexels);
    float flBlurLod = max(0.5 * log2(max(length_squared(vNormalDdx), length_squared(vNormalDdy))), g_flDiffuseNormalBlur);

    // Bumped like the sharp normal. The game scales only the xy of both normals before normalizing them
    vec3 vBlurredNormalMap = DecodeHemiOctahedronNormal(textureLod(g_tNormal, texCoord, flBlurLod).rg);
    vBlurredNormalMap = BlendNormalMapTowardsFlat(vBlurredNormalMap, g_flBumpStrength);
    vec3 vBlurredNormal = CalculateWorldNormal(vBlurredNormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

    mat.SoftNormal = normalize(mix(mat.Normal, vBlurredNormal, mat.SSSMask));

    AdjustRoughnessByGeometricNormal(mat);
    ResolveAmbientNormal(mat, texCoord);
    ResolveDiffuseSpecular(mat, vec3(0.04));

    mat.TransmissiveColor = g_vTransmissionColor.rgb * color.a;

    float mouthOcclusion = mix(1.0, g_flMouthInteriorBrightnessScale, mat.MouthMask);
    mat.TransmissiveColor *= mouthOcclusion;
    mat.AmbientOcclusion *= mouthOcclusion;

    FinishMaterial(mat);

    return mat;
}

void main()
{
    // Membrane blend mode: specular unaffected by opacity
    SurfaceOptions_t options = DefaultSurfaceOptions();
#if (F_TRANSLUCENT == 2)
    options.SpecularIgnoresOpacity = true;
#endif
    options.DirectDiffuseAO = g_flAmbientOcclusionDirectDiffuse;
    options.DirectSpecularAO = g_flAmbientOcclusionDirectSpecular;
    RenderSurface(g_tColor, options);
}
