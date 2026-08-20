#version 460

uniform sampler2D g_tSceneDepth;

void main()
{
    ivec2 sourceOrigin = ivec2(gl_FragCoord.xy) * ivec2(6, 3);
    ivec2 sourceMax = textureSize(g_tSceneDepth, 0) - 1;
    float nearestDepth = 0.0;

    for (int y = 0; y < 3; y++)
    {
        for (int x = 0; x < 6; x++)
        {
            ivec2 sourceCoord = min(sourceOrigin + ivec2(x, y), sourceMax);
            nearestDepth = max(nearestDepth, texelFetch(g_tSceneDepth, sourceCoord, 0).r);
        }
    }

    gl_FragDepth = nearestDepth;
}
