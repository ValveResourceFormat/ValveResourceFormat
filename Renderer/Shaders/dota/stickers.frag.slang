#version 460

// No specular light, the sticker adds its own reflection
#define IBL 0
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tNormal; // Sampler(UserConfig)
uniform samplerCube g_tCubeMapReflect; // SrgbRead(true)
uniform sampler2D g_tEffectMask; // SrgbRead(true)

uniform float g_flNormalStrength = 1.0;
uniform float g_flSpecularIntensity = 1.0;
uniform float g_flDiffuseLightingStrength = 0.75;
uniform float g_flMipSharpness = 1.0;
uniform float g_flCubeMapStrength = 1.0;
uniform float g_flBackgroundEffectCubemapDiffuseTint = 0.0;
uniform vec4 g_vBackgroundEffectMask = vec4(1.0);
uniform float g_flBackgroundBlendSharpness = 40.0;
uniform float g_flBackgroundColorAmount = 1.0;

// todo: accent effect layer, effect patterns and normals, hologram lookup, animated warping and heightmap parallax
vec3 GetStickerReflection(MaterialProperties_t mat, vec3 baseColor)
{
    // Sampled opposite the usual reflection vector
    vec3 reflectionDir = reflect(mat.ViewDir, mat.Normal);
    vec3 reflection = texture(g_tCubeMapReflect, reflectionDir).rgb;

    // Thin bright line along the horizon of the reflection
    float horizon = reflectionDir.z - reflectionDir.y / 3.0;
    reflection += saturate(horizon * -400.0) * pow(saturate(1.0 - abs(horizon)), 10.0) * 0.25;

    float NdotV = max(0.0, dot(mat.ViewDir, mat.Normal));
    float facingScale = mix(0.25, 1.0, 1.0 - NdotV);
    vec3 diffuseTint = mix(vec3(1.0), baseColor, g_flBackgroundEffectCubemapDiffuseTint);

    return reflection * facingScale * g_flCubeMapStrength * diffuseTint;
}

#if (S_MODE_DEPTH == 0)
// Unnormalized Phong lobe of the sun, unshadowed
vec3 GetStickerSunHighlight(MaterialProperties_t mat)
{
    vec3 reflectionDir = reflect(-mat.ViewDir, mat.Normal);
    float highlight = pow(saturate(dot(GetSunDirection(), reflectionDir)), 1000.0) * g_flSpecularIntensity;

    return GetSunColor() * highlight;
}
#endif

// Where its mask is set, the background effect scales the color by the distance from the middle of the sticker
vec3 ApplyStickerBackground(vec3 color, vec4 effectMask, vec2 texCoord)
{
    float backgroundMask = saturate(dot(g_vBackgroundEffectMask, effectMask) * g_flBackgroundBlendSharpness);
    float radialFalloff = length(texCoord - vec2(0.5)) * 1.5;
    vec3 backgroundColor = color * g_flBackgroundColorAmount * radialFalloff;

    return mix(color, backgroundColor, backgroundMask);
}

uniform float g_flAlphaTestReference = 0.5;

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    vec4 color = texture(g_tColor, texCoord, -g_flMipSharpness);
    vec4 effectMask = texture(g_tEffectMask, texCoord, -g_flMipSharpness);
    vec4 normalTexture = texture(g_tNormal, texCoord);

    mat.Albedo = color.rgb;

    InitOpacity(mat, color.a, 1.0, false, false, texCoord, g_flAlphaTestReference);

    mat.Albedo *= vVertexColorOut.rgb;
    mat.Albedo = ApplyStickerBackground(mat.Albedo, effectMask, texCoord);

    mat.NormalMap = ScaleNormalMapSlope(DecodeDxt5Normal(normalTexture), g_flNormalStrength);
    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

    mat.RoughnessTex = normalTexture.bb;

    AdjustRoughnessByGeometricNormal(mat);
    ResolveAmbientNormal(mat, texCoord);

    ResolveDiffuseSpecular(mat, vec3(0.04));

    mat.DiffuseColor *= g_flDiffuseLightingStrength;
    mat.IllumColor += GetStickerReflection(mat, color.rgb);
#if (S_MODE_DEPTH == 0)
    mat.IllumColor += GetStickerSunHighlight(mat);
#endif

    FinishMaterial(mat);

    return mat;
}

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.DirectSpecular = false;
    RenderSurface(g_tColor, options);
}
