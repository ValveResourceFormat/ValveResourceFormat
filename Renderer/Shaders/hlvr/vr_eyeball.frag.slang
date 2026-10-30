#version 460

#include "vr_eyeball_features.slang"

#define LIGHTMAP_VR_SHADING 1
#define IBL 1
#define SUBSURFACE 0
#define DIFFUSE_WRAP 1
// Direct diffuse is lit by mat.SoftNormal, which is the geometric normal here
#define DIFFUSE_SOFT_NORMAL 1
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

// The sclera wrap is the shared diffuse wrap, under the names of the material parameters
#define g_flDiffuseWrap g_flScleraDiffuseWrap
#define g_flDiffuseExponent g_flScleraDiffuseExponent

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_legacy_material.slang"
#include "../common/surface_shading.slang"

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tNormal; // Sampler(UserConfig)
uniform sampler2D g_tIris;
uniform sampler2D g_tIrisMask;

uniform float g_flCorneaScleraBumpStrength = 1.0;
uniform float g_flIrisBumpStrength = 1.0;
uniform float g_flParallaxStrength = 0.05;
uniform vec2 g_vReflectanceRange = vec2(0.0);

#if (F_SELF_ILLUM == 1)
    #include "../features/self_illum_ps.slang"
#endif

// Hemi-octahedral normal map with its slope scaled, normalized with a z of 1
vec3 DecodeEyeNormal(vec4 texel, float strength)
{
    vec3 normalMap = DecodeHemiOctahedronNormal(texel.ag);
    return normalize(vec3(normalMap.xy * strength, 1.0));
}

// The cornea and sclera as the material, and the iris as a second layer that is lit by its own normal, roughness and
// reflectance. The material holds the diffuse of both layers and the specular of the cornea, the iris adds its specular.
MaterialProperties_t GetEyeMaterial(vec2 texCoord, vec3 vertexNormals, out MaterialProperties_t iris, out float irisMask)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    vec4 normalTexture = texture(g_tNormal, texCoord);
    irisMask = texture(g_tIrisMask, texCoord).x;

    // The iris sits behind the cornea, so its textures shift with the view
    vec2 viewTangent = vec2(dot(mat.ViewDir, mat.Tangent), dot(mat.ViewDir, mat.Bitangent));
    vec2 irisTexCoord = texCoord - viewTangent * irisMask * g_flParallaxStrength;

    vec4 color = texture(g_tColor, irisTexCoord);
    vec4 irisTexture = texture(g_tIris, irisTexCoord);

    InitOpacity(mat, 1.0, 1.0, false, false, texCoord, 0.5);

    mat.Albedo = color.rgb * vVertexColorOut.rgb;

#if (F_SELF_ILLUM == 1)
    mat.IllumColor = SampleSelfIllumination(texCoord, mat.Albedo);
#endif

    mat.NormalMap = DecodeEyeNormal(normalTexture, g_flCorneaScleraBumpStrength);
    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);
    mat.SoftNormal = mat.GeometricNormal;
    mat.RoughnessTex = vec2(0.05);

    // The iris is lit by the plain cosine instead of the wrap
    mat.DiffuseWrapColor = vec3(1.0 - irisMask);

    FinishLitMaterial(mat, texCoord, vec3(0.04));

    iris = mat;
    iris.NormalMap = DecodeEyeNormal(irisTexture, g_flIrisBumpStrength);
    iris.Normal = CalculateWorldNormal(iris.NormalMap, iris.GeometricNormal, iris.Tangent, iris.Bitangent);
    iris.RoughnessTex = irisTexture.rb;
    iris.SpecularColor = vec3(RemapByRange(color.a, g_vReflectanceRange));

    AdjustRoughnessByGeometricNormal(iris);
    ResolveAmbientNormal(iris, texCoord);

    return mat;
}

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t iris;
    float irisMask;
    return GetEyeMaterial(texCoord, vertexNormals, iris, irisMask);
}

#if (S_MODE_DEPTH == 0)
// The iris mask scales the specular and the indirect light of the iris layer
vec4 ShadeEyeball(inout MaterialProperties_t mat, MaterialProperties_t iris, float irisMask, out LightingTerms_t lighting, SurfaceOptions_t options)
{
    vec3 diffuseLighting;
    vec3 specularLighting;
    LightSurface(mat, lighting, options, diffuseLighting, specularLighting);

    if (irisMask > 0.0)
    {
        LightingTerms_t irisLighting;
        vec3 irisDiffuseLighting;
        vec3 irisSpecularLighting;
        LightSurface(iris, irisLighting, options, irisDiffuseLighting, irisSpecularLighting);

        diffuseLighting += (irisLighting.DiffuseIndirect - lighting.DiffuseIndirect) * irisMask;
        specularLighting += irisSpecularLighting * irisMask;
    }

    // The game also darkens the eye with an occlusion texture, projected by a matrix it supplies at runtime
    return vec4(mat.DiffuseColor * diffuseLighting + specularLighting + mat.IllumColor, 1.0);
}
#endif

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();

#if (S_MODE_DEPTH != 0)
    RenderDepthPass();
#else
    MaterialProperties_t iris;
    float irisMask;
    MaterialProperties_t mat = GetEyeMaterial(vTexCoordOut, SwitchCentroidNormal(vNormalOut, vCentroidNormalOut), iris, irisMask);

    LightingTerms_t lighting;
    outputColor = ShadeEyeball(mat, iris, irisMask, lighting, options);

    FinishSurface(mat, lighting, options, g_tColor);
#endif
}
