#version 460

// Fragment shader body of the lit surfaces that blend a second layer, projected along the world axes, over the first.
// The entry file includes its features file first and defines GetMaterial() from GetBlendToTriplanarMaterial(). The
// membrane always projects its mask and resolves the diffuse and specular color of each layer before the blend.

// Rect lights shade as area lights
#define BARN_LIGHT_AREA_LIGHTS 1
#define LIGHTMAP_VR_SHADING 1
#define IBL 1
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"
#include "../features/triplanar_ps.slang"

uniform sampler2D g_tColorA; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tNormalA; // Sampler(UserConfig)
uniform sampler2D g_tColorB; // SrgbRead(true)
uniform sampler2D g_tNormalB;
uniform sampler2D g_tMask;
uniform float g_flMetalnessA = 0.0;
uniform float g_flMetalnessB = 0.0;

uniform sampler2D g_tTriplanarMask;
uniform float g_flTriplanarScale = 1.0;

in vec4 vColorBlendValues;

uniform float g_flAlphaTestReference = 0.5;

MaterialProperties_t GetBlendToTriplanarMaterial(vec2 texCoord, vec3 vertexNormals, bool xenMembrane)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    vec4 colorA = texture(g_tColorA, texCoord);
    vec4 normalTextureA = texture(g_tNormalA, texCoord);

    // Projected by the interpolated normal, not the centroid one
    vec3 projectionNormal = normalize(vNormalOut);
    Triplanar_t triplanar = GetTriplanar(vFragPosition, projectionNormal, g_flTriplanarScale);

#if (F_MASK_USES_TRIPLANAR_BLENDING == 1)
    float blendMask = SampleTriplanar(g_tTriplanarMask, triplanar).r;
#else
    float blendMask = xenMembrane ? SampleTriplanar(g_tTriplanarMask, triplanar).r : texture(g_tMask, texCoord).r;
#endif

    float blendFactor = ApplyBlendModulation(vColorBlendValues.r, blendMask, vColorBlendValues.g);

    vec4 colorB = SampleTriplanar(g_tColorB, triplanar);
    vec4 color = mix(colorA, colorB, blendFactor);

    mat.Albedo = color.rgb;
    InitOpacity(mat, 1.0, 1.0, false, false, texCoord, g_flAlphaTestReference);

    mat.NormalMap = DecodeHemiOctahedronNormal(normalTextureA.rg);
    mat.NormalMap = ApplyNormalMapStrength(mat.NormalMap, 1.0);
    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

    // The layers are blended after decoding, the second one is already in world space
    float roughnessB;
    vec3 normalB = SampleTriplanarNormal(g_tNormalB, triplanar, projectionNormal, roughnessB);

    mat.Normal = normalize(mix(mat.Normal, normalB, blendFactor));
    mat.RoughnessTex = vec2(mix(normalTextureA.b, roughnessB, blendFactor));

    mat.Metalness = mix(g_flMetalnessA, g_flMetalnessB, blendFactor);

    // Color alpha of both layers holds their occlusion
    mat.AmbientOcclusion = color.a;

    AdjustRoughnessByGeometricNormal(mat);
    ResolveAmbientNormal(mat, texCoord);

    if (xenMembrane)
    {
        // Each layer resolves its own diffuse and specular color before the blend
        vec3 diffuseA = colorA.rgb * (1.0 - g_flMetalnessA);
        vec3 diffuseB = colorB.rgb * (1.0 - g_flMetalnessB);
        vec3 specularA = mix(vec3(0.04), colorA.rgb, g_flMetalnessA);
        vec3 specularB = mix(vec3(0.04), colorB.rgb, g_flMetalnessB);

        mat.DiffuseColor = mix(diffuseA, diffuseB, blendFactor);
        mat.SpecularColor = mix(specularA, specularB, blendFactor);

        // todo: Xen foliage vertex motion, emissive mask, hand interaction glow and diffuse wrap transmission
    }
    else
    {
        ResolveDiffuseSpecular(mat, vec3(0.04));
    }

    FinishMaterial(mat);

    return mat;
}

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.OccludeDirectDiffuse = false;
    RenderSurface(g_tColorA, options);
}
