#version 460

#include "csgo_unlitgeneric_features.slang"

// The blend mode 2 cuts out by the alpha
#define ALPHA_TEST_BY_BLEND_MODE (F_BLEND_MODE == 2)

#define IBL 1
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture

uniform float g_flOpacityScale = 1.0;

#if (F_TWOTEXTURE == 1)
    in vec2 vTexCoord2Out;
    uniform sampler2D g_tColor2; // SrgbRead(true) Sampler(UserConfig)
#endif

uniform float g_flAlphaTestReference = 0.5;

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    vec4 color = texture(g_tColor, texCoord);

#if (F_TWOTEXTURE == 1)
    color *= texture(g_tColor2, vTexCoord2Out);
#endif

    mat.Albedo = color.rgb;

    float alpha = color.a * vVertexColorOut.a;
    InitOpacity(mat, alpha, g_flOpacityScale, (F_TRANSLUCENT == 1) || IsBlendModeTranslucent(F_BLEND_MODE), false, texCoord, g_flAlphaTestReference);

    mat.Albedo *= vVertexColorOut.rgb;

    // Blend mode 3 fades the color towards gray before the fog takes it
    if (F_BLEND_MODE == 3)
    {
        mat.Albedo = mix(vec3(0.5), mat.Albedo, vec3(alpha));
    }

    FinishUnlitMaterial(mat);
    return mat;
}

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.BlendMode = (F_BLEND_MODE == 3) ? 0 : F_BLEND_MODE;

    // Translucent, additive, multiply and mod-then-add materials fade into the fog instead of taking its color
    options.FogFadesAlpha = (F_BLEND_MODE == 1) || (F_BLEND_MODE >= 4);

    // Has no fullbright view of its own, so keeps its color, shown as it is written without post processing
    options.KeepColorInFullBright = true;

    RenderUnlitSurface(g_tColor, options);
}
