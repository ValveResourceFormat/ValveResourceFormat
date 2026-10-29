#version 460

// The entry file includes its game's features file first.

// Rect lights shade as area lights
#define BARN_LIGHT_AREA_LIGHTS 1
#define LIGHTMAP_VR_SHADING 1
#define DYNAMIC_LIGHT_ROUGHNESS_SHAPED_DIFFUSE 1
#define IBL 1
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"
#include "../common/surface_glass.slang"

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tNormal; // Sampler(UserConfig)
uniform sampler2D g_tAmbientOcclusion;
uniform sampler2D g_tTintColor; // SrgbRead(true)

uniform float g_flBumpStrength = 1.0;
uniform float g_flOpacityScale = 1.0;
uniform float g_flMetalness = 0.0;

uniform float g_flEdgeColorFalloff = 3.0;
uniform float g_flEdgeColorThickness = 0.1;
uniform float g_flEdgeColorMaxOpacity = 0.5;
// Black, unlike the edge color of the fresnel glass in texturing.slang
uniform vec3 g_vEdgeColor = vec3(0.0); // SrgbRead(true)

uniform float g_flEmissiveEdgeColorFalloff = 3.0;
uniform float g_flEmissiveEdgeColorThickness = 0.1;
uniform vec3 g_vEmissiveEdgeColor = vec3(0.0); // SrgbRead(true)
uniform vec3 g_vEmissiveColor = vec3(0.0); // SrgbRead(true)
uniform float g_flEmissiveStrength = 0.5;

#if (F_REFRACT == 1)
    uniform sampler2D g_tSceneColor; // reserved
    uniform float g_flRefractScale = 0.1;
    uniform bool g_bRefractBlur = true;
#endif

// The light the glass lets through, filled in by GetMaterial()
vec3 glassTransmission = vec3(1.0);

uniform float g_flAlphaTestReference = 0.5;

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    vec4 color = texture(g_tColor, texCoord);
    vec4 normalTexture = texture(g_tNormal, texCoord);

    mat.Albedo = color.rgb;

    InitOpacity(mat, color.a, g_flOpacityScale, true, true, texCoord, g_flAlphaTestReference);

    mat.Albedo *= vVertexColorOut.rgb;

    vec3 tintColor = texture(g_tTintColor, texCoord).rgb;
    mat.Metalness = g_flMetalness;

    mat.NormalMap = DecodeHemiOctahedronNormal(normalTexture.rg);
    mat.RoughnessTex.xy = normalTexture.bb;

    float normalMapAmount = (F_SCALE_NORMAL_MAP == 1) ? g_flNormalMapScaleFactor : g_flBumpStrength;
    mat.NormalMap = BlendNormalMapTowardsFlat(mat.NormalMap, normalMapAmount);

    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

    AdjustRoughnessByGeometricNormal(mat);
    ResolveAmbientNormal(mat, texCoord);

    mat.AmbientOcclusion = texture(g_tAmbientOcclusion, texCoord).r;

    ResolveDiffuseSpecular(mat, vec3(0.04));

    float edge = GetGlassEdge(mat, g_flEdgeColorThickness, g_flEdgeColorFalloff);
    float edgeOpacity = edge * g_flEdgeColorMaxOpacity * (1.0 - mat.Opacity);
    glassTransmission = tintColor * (1.0 - mat.Opacity) * mix(vec3(1.0), g_vEdgeColor, edgeOpacity);

    float emissiveEdge = GetGlassEdge(mat, g_flEmissiveEdgeColorThickness, g_flEmissiveEdgeColorFalloff);
    mat.IllumColor = mix(g_vEmissiveColor, g_vEmissiveEdgeColor, emissiveEdge) * g_flEmissiveStrength;

    FinishMaterial(mat);

    return mat;
}

#if (S_MODE_DEPTH == 0)

vec4 GetGlassOutput(MaterialProperties_t mat, vec3 diffuseLighting, vec3 transmissiveLighting, vec3 specularLighting)
{
    diffuseLighting += transmissiveLighting;

    vec3 color = mat.DiffuseColor * diffuseLighting * mat.Opacity + specularLighting + mat.IllumColor;
    vec3 transmission = glassTransmission;

    color = FogGlass(color, transmission, mat.PositionWS);

    float fade = vVertexColorOut.a;
    color *= fade;
    transmission = mix(vec3(1.0), transmission, fade);

#if (F_REFRACT == 1)
    return vec4(color + transmission * SampleRefraction(g_tSceneColor, mat, g_flRefractScale, g_bRefractBlur), 1.0);
#else
    return GetGlassBlendOutput(color, transmission);
#endif
}

#endif

void main()
{
    RenderGlass(g_tColor);
}
