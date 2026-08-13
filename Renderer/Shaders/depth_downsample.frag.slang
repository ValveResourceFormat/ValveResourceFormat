#version 460

uniform sampler2D g_tSceneDepth;
uniform vec2 g_vDownsampleFactor = vec2(2.0);

void main()
{
    ivec2 factor = ivec2(g_vDownsampleFactor);
    ivec2 sourceOrigin = ivec2(gl_FragCoord.xy) * factor;
    ivec2 sourceMax = textureSize(g_tSceneDepth, 0) - 1;

    float furthestDepth = 1.0;

    for (int y = 0; y < factor.y; y++)
    {
        for (int x = 0; x < factor.x; x++)
        {
            ivec2 sourceCoord = min(sourceOrigin + ivec2(x, y), sourceMax);
            furthestDepth = min(furthestDepth, texelFetch(g_tSceneDepth, sourceCoord, 0).r);
        }
    }

    gl_FragDepth = furthestDepth;
}
