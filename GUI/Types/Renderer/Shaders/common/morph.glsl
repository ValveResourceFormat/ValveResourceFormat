uniform sampler2D morphCompositeTexture;
uniform vec2 morphCompositeTextureSize;
uniform int morphVertexIdOffset;

vec2 getMorphUV()
{
    int vertexId = gl_VertexID + morphVertexIdOffset;
    return vec2(
        1.5/2048 + mod(vertexId, morphCompositeTextureSize.x * 1.0) / 2048.0,
        1 - (1.5/2048 + floor(vertexId / morphCompositeTextureSize.x * 1.0) / 2048.0)
    );
}

vec3 getMorphOffset()
{
    return texture(morphCompositeTexture, getMorphUV()).xyz;
}
