#version 460

public extern static const int F_MORPH_SUPPORTED = 0;

uniform sampler2D morphCompositeTexture;
uniform vec2 morphCompositeTextureSize;
uniform int morphVertexIdOffset;

public static uint vertexID;

#define bMorphed (morphVertexIdOffset != -1)

vec2 getMorphUV()
{
    int vertId = int(vertexID) + morphVertexIdOffset;
    return vec2(
        (1.5 + mod(vertId, morphCompositeTextureSize.x)) / 2048.0,
        1 - (1.5 + floor(vertId / morphCompositeTextureSize.x)) / 2048.0
    );
}

public vec3 getMorphOffset()
{
    if (F_MORPH_SUPPORTED == 1 && bMorphed)
    {
        return textureLod(morphCompositeTexture, getMorphUV(), 0).xyz;
    }

    return vec3(0, 0, 0);
}
