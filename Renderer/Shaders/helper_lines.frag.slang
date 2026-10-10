#version 460

in vec4 vtxColor;
out vec4 outputColor;

uniform sampler2D g_tSceneDepth; // reserved, resolved scene depth (reverse-Z window space)

// Drawn only where the scene does not hide it, or over everything
uniform bool g_bDepthTest;

// Fraction of depth a fragment may lie behind the scene and still show, so shapes on the
// scene's surfaces are not hidden
uniform float g_flDepthTolerance;

void main(void)
{
    if (g_bDepthTest)
    {
        float sceneDepth = texelFetch(g_tSceneDepth, ivec2(gl_FragCoord.xy), 0).r;

        // Reverse-Z, so nearer is larger
        if (gl_FragCoord.z < sceneDepth * (1.0 - g_flDepthTolerance))
        {
            discard;
        }
    }

    outputColor = vtxColor;
}
