#version 460

#include "csgo_glass_features.slang"

#define IBL 1
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0
// The tint texture's alpha lets light from behind the pane through, by the geometric normal
#define GLASS_BACKLIGHT 1

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"
#include "../common/surface_glass.slang"

uniform sampler2D g_tGlassDust; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tNormal; // Sampler(UserConfig)
uniform sampler2D g_tGlassTintColor; // SrgbRead(true)

uniform vec2 g_flTranslucencyRemap = vec2(0.0, 1.0);

#if (F_OPAQUE_CUBEMAP_REFRACTION == 1)
    uniform float g_flRefractMinRoughness = 0.0;
#endif

// The light the glass lets through, filled in by GetMaterial()
vec3 glassTransmission = vec3(1.0);
vec3 glassTintColor = vec3(1.0);

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    vec4 color = texture(g_tGlassDust, texCoord);
    vec4 normalTexture = texture(g_tNormal, texCoord);

    mat.Albedo = color.rgb;

    // Vertex tint colors the light let through, not the dust on the glass
    float dustOpacity = saturate(color.a * vVertexColorOut.a);
    mat.Opacity = mix(g_flTranslucencyRemap.x, g_flTranslucencyRemap.y, dustOpacity);
    vec4 tintTexture = texture(g_tGlassTintColor, texCoord);
    glassTintColor = tintTexture.rgb * vVertexColorOut.rgb;
    mat.TransmissiveColor = vec3(tintTexture.a * mat.Opacity);

    mat.NormalMap = DecodeHemiOctahedronNormal(normalTexture.rg);
    mat.RoughnessTex.xy = normalTexture.bb;
    mat.NormalMap = ApplyNormalMapStrength(mat.NormalMap, 1.0);

    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

    // Fading the glass out also makes it smoother
    mat.RoughnessTex *= vVertexColorOut.a;

    AdjustRoughnessByGeometricNormal(mat);
    ResolveAmbientNormal(mat, texCoord);
    ResolveDiffuseSpecular(mat, vec3(0.04));

    // Light reflected off the front leaves, and the tint gets deeper at grazing angles inside the pane
    float viewDotGeometricNormal = max(0.0, dot(mat.ViewDir, mat.GeometricNormal));
    float refractedCosine = sqrt(1.0 - (1.0 - viewDotGeometricNormal * viewDotGeometricNormal) / 2.25);
    float fresnel = mix(0.04, 1.0, pow(1.0 - max(0.0, dot(mat.ViewDir, mat.Normal)), 5.0));
    glassTransmission = (1.0 - fresnel) * pow(glassTintColor, vec3(1.0 / max(0.01, refractedCosine))) * (1.0 - mat.Opacity);

    FinishMaterial(mat);

    return mat;
}

#if (S_MODE_DEPTH == 0)

#if (F_OPAQUE_CUBEMAP_REFRACTION == 1)
    vec3 SampleCubemapRefraction(MaterialProperties_t mat)
    {
        float roughness = mix(g_flRefractMinRoughness, 1.0, dot(mat.RoughnessTex, vec2(0.5)));
        vec3 refracted = refract(-mat.ViewDir, mat.Normal, 0.66);
        float lod = GetEnvMapLOD(sqrt(roughness));

        // Unlike the game, the cubemap is not normalized by the baked lighting
    #if (ENVMAP_PER_OBJECT)
        return SampleObjectEnvMap(refracted, mat.Normal, roughness, lod);
    #else
        return BlendEnvMaps(refracted, roughness, lod);
    #endif
    }
#endif

vec4 GetGlassOutput(MaterialProperties_t mat, vec3 diffuseLighting, vec3 transmissiveLighting, vec3 specularLighting)
{
    diffuseLighting += transmissiveLighting;

    vec3 color = mat.DiffuseColor * diffuseLighting * mat.Opacity + specularLighting + mat.IllumColor;
    vec3 transmission = glassTransmission;

    color += transmission * mat.Opacity * mat.Albedo * diffuseLighting * pow(glassTintColor, vec3(1.6));

    // Opaque dust is not fogged. Exact for one fog type, the game scales each fog type's amount on its own.
    vec3 foggedColor = FogGlass(color, transmission, mat.PositionWS);
    color = mix(foggedColor, color, mat.Opacity);

#if (F_OPAQUE_CUBEMAP_REFRACTION == 1)
    return vec4(color + transmission * SampleCubemapRefraction(mat), 1.0);
#else
    return GetGlassBlendOutput(color, transmission);
#endif
}

#endif

void main()
{
    RenderGlass(g_tGlassDust);
}
