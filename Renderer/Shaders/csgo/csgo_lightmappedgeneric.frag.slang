#version 460

#include "csgo_lightmappedgeneric_features.slang"

#define IBL (F_SPECULAR_INDIRECT == 1)
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tLayer1NormalRoughness; // Sampler(UserConfig)
uniform sampler2D g_tLayer1AmbientOcclusion;
uniform vec3 g_vLayer1Tint = vec3(1.0); // SrgbRead(true)
uniform float g_flMetalness = 0.0;
uniform float g_flOpacityScale = 1.0;

#if (F_LAYERS > 0)
    uniform sampler2D g_tLayer2Color; // SrgbRead(true) Sampler(UserConfig)
    uniform sampler2D g_tLayer2NormalRoughness; // Sampler(UserConfig)
    uniform sampler2D g_tLayer2AmbientOcclusion;
    uniform vec3 g_vLayer2Tint = vec3(1.0); // SrgbRead(true)
#endif

#if (F_TEXTURETRANSFORMS == 1)
    uniform vec2 g_vLayer1TexCoordScale = vec2(1.0);
    uniform vec2 g_vLayer1TexCoordOffset = vec2(0.0);
    uniform vec2 g_vLayer1TexCoordCenter = vec2(0.5);
    uniform float g_flLayer1TexCoordRotation = 0.0;
    uniform vec2 g_vLayer1NormalTexCoordScale = vec2(1.0);
    uniform vec2 g_vLayer1NormalTexCoordOffset = vec2(0.0);
    uniform vec2 g_vLayer1NormalTexCoordCenter = vec2(0.5);
    uniform float g_flLayer1NormalTexCoordRotation = 0.0;

    #if (F_LAYERS > 0)
        uniform vec2 g_vLayer2TexCoordScale = vec2(1.0);
        uniform vec2 g_vLayer2TexCoordOffset = vec2(0.0);
        uniform vec2 g_vLayer2TexCoordCenter = vec2(0.5);
        uniform float g_flLayer2TexCoordRotation = 0.0;
        uniform vec2 g_vLayer2NormalTexCoordScale = vec2(1.0);
        uniform vec2 g_vLayer2NormalTexCoordOffset = vec2(0.0);
        uniform vec2 g_vLayer2NormalTexCoordCenter = vec2(0.5);
        uniform float g_flLayer2NormalTexCoordRotation = 0.0;
    #endif

    #if (F_FANCY_BLENDING > 0)
        uniform vec2 g_vBlendModulateTexCoordScale = vec2(1.0);
        uniform vec2 g_vBlendModulateTexCoordOffset = vec2(0.0);
        uniform vec2 g_vBlendModulateTexCoordCenter = vec2(0.5);
        uniform float g_flBlendModulateTexCoordRotation = 0.0;
    #endif
#endif

#if (F_DETAILTEXTURE > 0)
    uniform sampler2D g_tLayer1Detail; // Sampler(UserConfig)
    uniform vec2 g_vLayer1DetailScale = vec2(4.0);
    uniform vec4 g_vLayer1DetailTintAndBlend = vec4(1.0);
#endif

#if (F_DETAILTEXTURE == 2) && (F_LAYERS > 0)
    uniform sampler2D g_tLayer2Detail; // Sampler(UserConfig)
    uniform vec2 g_vLayer2DetailScale = vec2(4.0);
    uniform vec4 g_vLayer2DetailTintAndBlend = vec4(1.0);
#endif

// Translucent overlays that are not alpha tested fade out by a reference alpha
#if (F_OVERLAY == 1) && (F_TRANSLUCENT == 1) && (F_ALPHA_TEST == 0)
    uniform float g_flAlphaTestReference = 0.01;
#endif

#if (F_FANCY_BLENDING > 0)
    uniform sampler2D g_tBlendModulation; // Sampler(UserConfig)
    uniform float g_flBlendSoftness;
#endif

#if (F_FANCY_BLENDING > 1)
    uniform float g_flLayerBorderStrength = 0.5;
    uniform float g_flLayerBorderOffset = 0.0;
    uniform float g_flLayerBorderSoftness = 0.5;
    uniform vec3 g_vLayerBorderTint = vec3(1.0); // SrgbRead(true)

    uniform float g_flBevelBlendWidth = 4.0;
    uniform float g_flBevelBlendSharpness = 2.0;
    uniform float g_flBevelBlendStrength = 0.0;
#endif

#if (F_LAYERS > 0)
    in vec4 vColorBlendValues;

    #include "../features/blend_second_layer.slang"
#endif

// How the second layer sits over the first
struct LayerBlend_t
{
    float Factor;
    float VertexBlend;
    float Mask;
    vec2 Layer2TexCoord;
};

// The other layers' transforms apply on top of the first layer's
void GetLayer1TexCoords(vec2 texCoord, out vec2 layer1TexCoord, out vec2 layer1NormalTexCoord)
{
    layer1TexCoord = texCoord;
    layer1NormalTexCoord = texCoord;

#if (F_TEXTURETRANSFORMS == 1)
    layer1TexCoord = TransformSecondaryTexCoord(texCoord, g_flLayer1TexCoordRotation, g_vLayer1TexCoordScale, g_vLayer1TexCoordOffset, g_vLayer1TexCoordCenter);
    layer1NormalTexCoord = TransformSecondaryTexCoord(layer1TexCoord, g_flLayer1NormalTexCoordRotation, g_vLayer1NormalTexCoordScale, g_vLayer1NormalTexCoordOffset, g_vLayer1NormalTexCoordCenter);
#endif
}

#if (F_LAYERS > 0)
void GetLayer2TexCoords(vec2 layer1TexCoord, out vec2 layer2TexCoord, out vec2 layer2NormalTexCoord, out vec2 blendModTexCoord)
{
    layer2TexCoord = layer1TexCoord;
    layer2NormalTexCoord = layer1TexCoord;
    blendModTexCoord = layer1TexCoord;

#if (F_TEXTURETRANSFORMS == 1)
    layer2TexCoord = TransformSecondaryTexCoord(layer1TexCoord, g_flLayer2TexCoordRotation, g_vLayer2TexCoordScale, g_vLayer2TexCoordOffset, g_vLayer2TexCoordCenter);
    layer2NormalTexCoord = TransformSecondaryTexCoord(layer1TexCoord, g_flLayer2NormalTexCoordRotation, g_vLayer2NormalTexCoordScale, g_vLayer2NormalTexCoordOffset, g_vLayer2NormalTexCoordCenter);

    #if (F_FANCY_BLENDING > 0)
        blendModTexCoord = TransformSecondaryTexCoord(layer1TexCoord, g_flBlendModulateTexCoordRotation, g_vBlendModulateTexCoordScale, g_vBlendModulateTexCoordOffset, g_vBlendModulateTexCoordCenter);
    #endif
#endif
}
#endif

#if (F_FANCY_BLENDING > 0)
// 0: VertexBlend 1: BlendModulateTexture,rg 2: NewLayerBlending,g 3: NewLayerBlending,a
float GetBlendFactor(vec4 blendModTexel, float vertexBlend, out float blendMask)
{
    blendMask = (F_FANCY_BLENDING == 3) ? blendModTexel.a : blendModTexel.g;
    float softness = (F_FANCY_BLENDING == 1) ? blendModTexel.r : g_flBlendSoftness;

    return ApplyBlendModulation(vertexBlend, blendMask, softness);
}
#endif

#if (F_FANCY_BLENDING > 1)
// Tints the first layer along the edge of the blend, the offset is stored negated
vec3 ApplyLayerBorder(vec3 color, float vertexBlend, float blendMask)
{
    float borderBlend = saturate(vertexBlend + g_flLayerBorderOffset);
    float borderEdge = ApplyBlendModulation(borderBlend, blendMask, g_flLayerBorderSoftness);
    float borderAmount = (1.0 - abs(borderEdge * 2.0 - 1.0)) * g_flLayerBorderStrength;

    return color * mix(vec3(1.0), g_vLayerBorderTint, borderAmount);
}
#endif

// Mixes the second layer into the color and the normal texture
LayerBlend_t BlendLayers(inout vec4 color, inout vec4 normalTexture, vec2 layer1TexCoord)
{
    LayerBlend_t blend;
    blend.Factor = 0.0;
    blend.VertexBlend = 0.0;
    blend.Mask = 0.0;
    blend.Layer2TexCoord = layer1TexCoord;

#if (F_LAYERS > 0)
    vec2 layer2NormalTexCoord;
    vec2 blendModTexCoord;
    GetLayer2TexCoords(layer1TexCoord, blend.Layer2TexCoord, layer2NormalTexCoord, blendModTexCoord);

    Layer2_t layer2;
    layer2.TexCoord = blend.Layer2TexCoord;
    layer2.Color = texture(g_tLayer2Color, blend.Layer2TexCoord);
    layer2.NormalTexture = texture(g_tLayer2NormalRoughness, layer2NormalTexCoord);
    layer2.Color.rgb *= g_vLayer2Tint;

    blend.VertexBlend = vColorBlendValues.r;
    blend.Factor = blend.VertexBlend;

    #if (F_FANCY_BLENDING > 0)
        vec4 blendModTexel = texture(g_tBlendModulation, blendModTexCoord);
        blend.Factor = GetBlendFactor(blendModTexel, blend.VertexBlend, blend.Mask);
    #endif

    #if (F_FANCY_BLENDING > 1)
        color.rgb = ApplyLayerBorder(color.rgb, blend.VertexBlend, blend.Mask);
    #endif

    BlendLayer2(color, normalTexture, layer2, blend.Factor);
#endif

    return blend;
}

// Both details tile the first layer's texcoords, and modulate the blended color around mid grey
void ApplyDetailTextures(inout vec4 color, vec2 layer1TexCoord, float blendFactor)
{
#if (F_DETAILTEXTURE > 0)
    vec3 detailColor = texture(g_tLayer1Detail, layer1TexCoord * g_vLayer1DetailScale).rgb * g_vLayer1DetailTintAndBlend.rgb;
    float detailBlend = g_vLayer1DetailTintAndBlend.a;

    #if (F_DETAILTEXTURE == 2) && (F_LAYERS > 0)
        vec3 detailColor2 = texture(g_tLayer2Detail, layer1TexCoord * g_vLayer2DetailScale).rgb * g_vLayer2DetailTintAndBlend.rgb;
        detailColor = mix(detailColor, detailColor2, blendFactor);
        detailBlend = mix(detailBlend, g_vLayer2DetailTintAndBlend.a, blendFactor);
    #endif

    color.rgb *= mix(vec3(1.0), detailColor * 2.0, detailBlend);
#endif
}

// Translucent overlays fade out by the reference, so the faintest part of the texture is not drawn
float FadeOverlayOpacity(float opacity)
{
#if (F_OVERLAY == 1) && (F_TRANSLUCENT == 1) && (F_ALPHA_TEST == 0)
    return max(opacity - g_flAlphaTestReference, 0.0);
#else
    return opacity;
#endif
}

// Bends the normal towards the faces of the surface displaced along the blend, which rounds off the layer edge
void ApplyBevelNormal(inout MaterialProperties_t mat, LayerBlend_t blend)
{
#if (F_LAYERS > 0) && (F_FANCY_BLENDING > 1)
    float bevelBlend = ApplyBlendModulation(blend.VertexBlend, blend.Mask, g_flBlendSoftness * g_flBevelBlendWidth);
    vec3 bevelPosition = vFragPosition + vNormalOut * (bevelBlend * g_flBevelBlendStrength);
    vec3 bevelNormal = normalize(cross(-dFdyFine(bevelPosition), dFdxFine(bevelPosition)));
    float bevelAmount = saturate((1.0 - 2.0 * abs(0.5 - bevelBlend)) * g_flBevelBlendSharpness);
    mat.Normal = normalize(mat.Normal + bevelNormal * bevelAmount);
#endif
}

float GetOcclusion(vec2 layer1TexCoord, LayerBlend_t blend)
{
    float occlusion = texture(g_tLayer1AmbientOcclusion, layer1TexCoord).r;

#if (F_LAYERS > 0)
    float layer2Occlusion = texture(g_tLayer2AmbientOcclusion, blend.Layer2TexCoord).r;
    occlusion = mix(occlusion, layer2Occlusion, blend.Factor);
#endif

    return occlusion;
}

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    vec2 layer1TexCoord;
    vec2 layer1NormalTexCoord;
    GetLayer1TexCoords(texCoord, layer1TexCoord, layer1NormalTexCoord);

    vec4 color = texture(g_tColor, layer1TexCoord);
    vec4 normalTexture = texture(g_tLayer1NormalRoughness, layer1NormalTexCoord);
    color.rgb *= g_vLayer1Tint;

    LayerBlend_t blend = BlendLayers(color, normalTexture, layer1TexCoord);
    ApplyDetailTextures(color, layer1TexCoord, blend.Factor);

    mat.Albedo = color.rgb;

    // The object alpha fades translucent surfaces, and it is applied twice
    bool translucent = (F_TRANSLUCENT == 1);
    float alpha = translucent ? color.a * vVertexColorOut.a : color.a;
    InitOpacity(mat, alpha, g_flOpacityScale, translucent, translucent, texCoord, g_flAlphaTestReference);
    mat.Opacity = FadeOverlayOpacity(mat.Opacity);

    mat.Albedo *= vVertexColorOut.rgb;

    mat.NormalMap = DecodeHemiOctahedronNormal(normalTexture.rg);
    mat.RoughnessTex.xy = normalTexture.bb;

    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);
    ApplyBevelNormal(mat, blend);

    mat.AmbientOcclusion = GetOcclusion(layer1TexCoord, blend);
    mat.Metalness = g_flMetalness;

    FinishLitMaterial(mat, texCoord, vec3(0.04));

    return mat;
}

void main()
{
#if (F_LAYERS > 0)
    vec3 blendValues = vColorBlendValues.rgb;
#else
    vec3 blendValues = vec3(0.0);
#endif

    // Lights are diffuse only unless the material asks for direct specular, and the occlusion texture darkens only the baked lighting
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.DirectSpecular = F_SPECULAR_DIRECT == 1;
    options.OccludeDirectDiffuse = false;
    options.BlendValues = blendValues;
    RenderSurface(g_tColor, options);
}
