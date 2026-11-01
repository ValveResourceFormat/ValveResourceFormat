#version 460

// Rect lights shade as area lights
#define BARN_LIGHT_AREA_LIGHTS 1
#define LIGHTMAP_VR_SHADING 1
#define DYNAMIC_LIGHT_ROUGHNESS_SHAPED_DIFFUSE 1
#define IBL 1
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0
// Light from behind the pane passes through the dust
#define GLASS_BACKLIGHT 1

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"
#include "../common/surface_glass.slang"

uniform sampler2D g_tGlassDust; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tGlassBackfaceDust; // SrgbRead(true)
uniform sampler2D g_tGlassTintColor; // SrgbRead(true)
uniform sampler2D g_tNormal; // Sampler(UserConfig)

uniform int F_CHEAP_DULL_GLASS;

uniform float g_flThickness = 1.0;
uniform float g_flDustForwardScattering = 0.5;
uniform float g_flShadowSoftness = 1.0;

// What GetMaterial() samples for GetGlassOutput()
vec3 glassTintColor = vec3(1.0);
vec4 glassDust = vec4(0.0);
vec4 glassBackfaceDust = vec4(0.0);
// The alpha of the dust of one face seen at the coordinates of the other, which shadows the light passing through it
float glassDustShadow = 0.0;
float glassBackfaceDustShadow = 0.0;

// The coordinates where the view ray meets the dust on the back face, a thickness behind the front face
vec2 GetBackfaceTexCoord(MaterialProperties_t mat, vec2 texCoord)
{
    vec3 positionDx = dFdx(mat.PositionWS);
    vec3 positionDy = dFdy(mat.PositionWS);
    vec2 texCoordDx = dFdx(texCoord);
    vec2 texCoordDy = dFdy(texCoord);

    // Texture coordinates per world unit along the tangent and bitangent, from the screen axis that moves further along each
    vec2 tangentDelta = vec2(dot(positionDx, mat.Tangent), dot(positionDy, mat.Tangent));
    vec2 bitangentDelta = vec2(dot(positionDx, mat.Bitangent), dot(positionDy, mat.Bitangent));
    vec2 texCoordPerUnit;

    if (abs(tangentDelta.x) > abs(tangentDelta.y))
    {
        texCoordPerUnit.x = texCoordDx.x / tangentDelta.x;
    }
    else
    {
        texCoordPerUnit.x = texCoordDy.x / tangentDelta.y;
    }

    if (abs(bitangentDelta.x) > abs(bitangentDelta.y))
    {
        texCoordPerUnit.y = texCoordDx.y / bitangentDelta.x;
    }
    else
    {
        texCoordPerUnit.y = texCoordDy.y / bitangentDelta.y;
    }

    vec2 viewOnSurface = vec2(dot(mat.ViewDir, mat.Tangent), dot(mat.ViewDir, mat.Bitangent));
    float depthScale = -g_flThickness / dot(mat.GeometricNormal, mat.ViewDir);

    return texCoord + texCoordPerUnit * viewOnSurface * depthScale;
}

// The alpha of a texture at the mip level of the screen footprint of the coordinates, at least as blurry as the shadow softness
float SampleDustShadow(sampler2D dustTexture, vec2 texCoord)
{
    vec2 texels = texCoord * vec2(textureSize(dustTexture, 0));
    vec2 texelsDx = dFdx(texels);
    vec2 texelsDy = dFdy(texels);
    float footprintLod = 0.5 * log2(max(dot(texelsDx, texelsDx), dot(texelsDy, texelsDy)));

    return textureLod(dustTexture, texCoord, max(footprintLod, g_flShadowSoftness)).a;
}

// g_tNormal is read at the mesh texcoords with no crack mask, so whether a pane shows cracks depends
// on the material: an intact func_shatterglass pane uses the undamaged material, whose normal is flat,
// and the entity swaps it in for the compiled panel's broken-glass material.
MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    vec2 backfaceTexCoord = GetBackfaceTexCoord(mat, texCoord);

    glassTintColor = texture(g_tGlassTintColor, texCoord).rgb;
    glassDust = texture(g_tGlassDust, texCoord);
    glassBackfaceDust = texture(g_tGlassBackfaceDust, backfaceTexCoord);

    if (F_CHEAP_DULL_GLASS == 0)
    {
        glassDustShadow = SampleDustShadow(g_tGlassDust, backfaceTexCoord);
        glassBackfaceDustShadow = SampleDustShadow(g_tGlassBackfaceDust, texCoord);
    }

    vec4 normalTexture = texture(g_tNormal, texCoord);

    mat.Albedo = glassDust.rgb;
    mat.Opacity = glassDust.a;
    mat.TransmissiveColor = vec3(1.0);

    mat.NormalMap = DecodeHemiOctahedronNormal(normalTexture.rg);
    mat.RoughnessTex.xy = normalTexture.bb;
    mat.NormalMap = ApplyNormalMapStrength(mat.NormalMap, 1.0);

    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

    FinishLitMaterial(mat, texCoord, vec3(0.04));

    return mat;
}

#if (S_MODE_DEPTH == 0)

vec4 GetGlassOutput(MaterialProperties_t mat, vec3 diffuseLighting, vec3 transmissiveLighting, vec3 specularLighting)
{
    vec3 tint = pow(glassTintColor, vec3(1.6));
    float scattering = g_flDustForwardScattering;

    // The dust on the front face is lit from the front, and from behind through the tint and the dust on the back face
    vec3 frontDustLighting = mix(diffuseLighting, transmissiveLighting * tint * (1.0 - glassBackfaceDustShadow), scattering);
    vec3 color = glassDust.a * glassDust.rgb * frontDustLighting;

    float clearGlass = 1.0 - glassDust.a;
    vec3 transmission = vec3(clearGlass);

    if (F_CHEAP_DULL_GLASS == 0)
    {
        color += clearGlass * specularLighting;

        float fresnel = mix(0.04, 1.0, pow(1.0 - max(0.0, dot(mat.ViewDir, mat.Normal)), 5.0));
        transmission *= 1.0 - fresnel;
    }

    // The tint gets deeper at grazing angles inside the pane
    float viewDotGeometricNormal = max(0.0, dot(mat.ViewDir, mat.GeometricNormal));
    float refractedCosine = sqrt(1.0 - (1.0 - viewDotGeometricNormal * viewDotGeometricNormal) / 2.25);
    transmission *= pow(glassTintColor, vec3(1.0 / max(0.01, refractedCosine)));

    // The dust on the back face is lit from behind, and from the front through the tint and the dust on the front face
    vec3 backDustLighting = mix(diffuseLighting * tint * (1.0 - glassDustShadow), transmissiveLighting, scattering);
    color += transmission * glassBackfaceDust.a * glassBackfaceDust.rgb * backDustLighting;
    transmission *= 1.0 - glassBackfaceDust.a;

    color = FogGlass(color, transmission, mat.PositionWS);

    return GetGlassBlendOutput(color, transmission);
}

#endif

void main()
{
    RenderGlass(g_tGlassDust);
}
