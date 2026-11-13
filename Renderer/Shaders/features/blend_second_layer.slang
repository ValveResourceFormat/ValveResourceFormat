#version 460

#ifndef VRF_BLEND_SECOND_LAYER_INCLUDE
#define VRF_BLEND_SECOND_LAYER_INCLUDE

// Second color and normal layer of the blend shaders, mixed over the first by a factor the shader picks.
// The shader declares the textures of the layer under the names used by its game,
// and declares vColorBlendValues when the vertex shader writes it.

struct Layer2_t
{
    vec2 TexCoord;
    vec4 Color;
    vec4 NormalTexture;
};

Layer2_t SampleLayer2(sampler2D colorTexture, sampler2D normalTexture, vec2 texCoord, vec2 texCoordScale)
{
    Layer2_t layer2;
    layer2.TexCoord = texCoord * texCoordScale;
    layer2.Color = texture(colorTexture, layer2.TexCoord);
    layer2.NormalTexture = texture(normalTexture, layer2.TexCoord);
    return layer2;
}

void BlendLayer2(inout vec4 color, inout vec4 normalTexture, Layer2_t layer2, float blendFactor)
{
    color = mix(color, layer2.Color, blendFactor);
    // It's more correct to blend normals after decoding, but it's not actually how S2 does it
    normalTexture = mix(normalTexture, layer2.NormalTexture, blendFactor);
}

#endif
