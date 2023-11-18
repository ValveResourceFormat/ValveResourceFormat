uniform sampler2D morphCompositeTexture;
uniform vec2 morphCompositeTextureSize;

vec2 getMorphUV()
{
    int vertexId = gl_VertexID + 0 /* TODO: some kind of mesh vertex offset here */;
    return vec2(
        1.5/2048 + mod(vertexId, morphCompositeTextureSize.x * 1.0) / 2048.0,
        1 - (1.5/2048 + floor(vertexId / morphCompositeTextureSize.x * 1.0) / 2048.0)
    );
}

vec3 getMorphOffset()
{
    /*if (gl_VertexID == 34 || gl_VertexID == 26) {
        return vec3(0,0,20);
    }
    else {
    return vec3(0,0,0);
    }*/
    return texture(morphCompositeTexture, getMorphUV()).xyz;
}
