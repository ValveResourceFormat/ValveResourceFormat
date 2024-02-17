#version 460

in vec4 vtxColor;
out vec4 outputColor;

uniform sampler2DMS g_tDepth;

void main(void) {
    outputColor = vtxColor;

    vec4 depth = texelFetch(g_tDepth, ivec2(gl_FragCoord.xy), 0);
    float sceneDepth = depth.r;
    float currentDepth = gl_FragCoord.z;

    if (sceneDepth > currentDepth) {
        outputColor.a = 0.1;
    }
}
