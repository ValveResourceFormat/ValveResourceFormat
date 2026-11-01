#version 460

#include "vr_simple_2layer_parallax_features.slang"

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
#include "../common/surface_parallax.slang"

uniform sampler2D g_tColorA; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tNormalA; // Sampler(UserConfig)
uniform sampler2D g_tColorB; // SrgbRead(true)
uniform sampler2D g_tNormalB;
uniform sampler2D g_tMask;

uniform float g_flMetalnessA = 0.0;
uniform float g_flMetalnessB = 0.0;
uniform float g_flMaterialATintAmount = 1.0;
uniform float g_flMaterialBTintAmount = 1.0;
uniform float g_flFrontFaceDisplacement = 0.0;
uniform float g_flBackFaceDisplacement = 0.0;
uniform vec2 g_vFrontTexCoordScale = vec2(1.0);
uniform vec2 g_vBackTexCoordScale = vec2(1.0);
uniform vec2 g_vFrontTexCoordOffset = vec2(0.0);
uniform vec2 g_vBackTexCoordOffset = vec2(0.0);

uniform float g_flAlphaTestReference = 0.5;

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    vec2 parallaxDir = GetParallaxDirection(mat);
    vec2 frontUV = GetLayerTexCoord(texCoord, g_vFrontTexCoordScale, g_vFrontTexCoordOffset, g_flFrontFaceDisplacement, parallaxDir);
    vec2 backUV = GetLayerTexCoord(texCoord, g_vBackTexCoordScale, g_vBackTexCoordOffset, g_flBackFaceDisplacement, parallaxDir);

    vec4 frontColor = texture(g_tColorA, frontUV);
    vec4 frontNormal = texture(g_tNormalA, frontUV);
    vec4 backColor = texture(g_tColorB, backUV);
    vec4 backNormal = texture(g_tNormalB, backUV);
    float backBlend = 1.0 - texture(g_tMask, frontUV).r;

    frontColor.rgb *= mix(vec3(1.0), vVertexColorOut.rgb, g_flMaterialATintAmount);
    backColor.rgb *= mix(vec3(1.0), vVertexColorOut.rgb, g_flMaterialBTintAmount);

    // Both layers pack ambient occlusion in color alpha, and roughness in the normal map
    vec4 color = mix(frontColor, backColor, backBlend);
    vec4 normalTexture = mix(frontNormal, backNormal, backBlend);

    mat.Albedo = color.rgb;
    InitOpacity(mat, 1.0, 1.0, false, false, texCoord, g_flAlphaTestReference);

    mat.Metalness = mix(g_flMetalnessA, g_flMetalnessB, backBlend);

    mat.NormalMap = DecodeHemiOctahedronNormal(normalTexture.rg);
    mat.RoughnessTex.xy = normalTexture.bb;
    mat.NormalMap = ApplyNormalMapStrength(mat.NormalMap, 1.0);
    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

    mat.AmbientOcclusion = color.a;

    FinishLitMaterial(mat, texCoord, vec3(0.04));

    return mat;
}

void main()
{
    // Ambient occlusion darkens indirect light and specular, direct diffuse light stays unoccluded
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.OccludeDirectDiffuse = false;
    RenderSurface(g_tColorA, options);
}
