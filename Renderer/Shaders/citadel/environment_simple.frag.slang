#version 460

// Rect lights shade as area lights, the way Deadlock does it
#define BARN_LIGHT_AREA_LIGHTS 2
#define IBL 1
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

// Deadlock blends the light probe volumes in every lightmap version
#define ENABLE_LPV_BLENDING 1

#define SCREEN_SPACE_AO 1

#include "../common/surface_base_ps.slang"
#include "../common/citadel.slang"
#include "../common/surface_shading.slang"

in vec3 vColorBlendValues;

uniform sampler2D g_tColor; // SrgbRead(true) RepresentativeTexture

// One texture carries albedo with the vertex colour tint mask in alpha. There is no normal map, and
// roughness, metalness and self illumination are all vertex painted.
MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    vec4 color = texture(g_tColor, texCoord);

    mat.Albedo = mix(color.rgb, color.rgb * vVertexColorOut.rgb, color.a);
    mat.IllumColor = mat.Albedo * vColorBlendValues.z;
    mat.Metalness = vColorBlendValues.y;
    mat.RoughnessTex = vec2(vColorBlendValues.x);

    mat.Normal = mat.GeometricNormal;
    AdjustRoughnessByGeometricNormal(mat);

    mat.AmbientNormal = mat.Normal;
    mat.AmbientGeometricNormal = mat.GeometricNormal;

    ResolveDiffuseSpecularCitadel(mat);

    return mat;
}

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.BlendValues = vec3(vColorBlendValues.xy, 0.0);
    RenderSurface(g_tColor, options);
}
