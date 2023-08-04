#version 460

// https://github.com/BabylonJS/Babylon.js/blob/bd7351cfc97884d3293d5858b8a0190cda640b2f/packages/dev/materials/src/grid/grid.fragment.fx
// https://asliceofrendering.com/scene%20helper/2020/01/05/InfiniteGrid/

float near = 0.01;
float far = 100;

in vec3 vtxPosition;
in vec3 nearPoint;
in vec3 farPoint;

out vec4 outputColor;

#include "common/ViewConstants.glsl"

vec4 grid(vec3 fragPos3D, float scale, bool drawAxis) {
    vec2 coord = fragPos3D.xy / scale;
    vec2 derivative = fwidth(coord);
    vec2 grid = abs(fract(coord - 0.5) - 0.5) / derivative;
    float line = min(grid.x, grid.y);
    vec4 color = vec4(0.1, 0.1, 0.15, 1.0 - min(line, 1.0));

    if (drawAxis && fragPos3D.x > -1 && fragPos3D.x < 1) {
        color.x = 0.5;
    }

    if (drawAxis && fragPos3D.y > -1 && fragPos3D.y < 1) {
        color.y = 0.7;
    }

    return color;
}

float computeDepth(vec3 pos) {
    vec4 clip_space_pos = g_matWorldToProjection * g_matWorldToView * vec4(pos.xyz, 1.0);
    return (clip_space_pos.z / clip_space_pos.w);
}

float computeLinearDepth(vec3 pos) {
    vec4 clip_space_pos = g_matWorldToProjection * g_matWorldToView * vec4(pos.xyz, 1.0);
    float clip_space_depth = (clip_space_pos.z / clip_space_pos.w) * 2.0 - 1.0; // put back between -1 and 1
    float linearDepth = (2.0 * near * far) / (far + near - clip_space_depth * (far - near)); // get linear value between 0.01 and 100
    return linearDepth / far; // normalize
}

void main() {
    float t = -nearPoint.z / (farPoint.z - nearPoint.z);

    vec3 fragPos3D = nearPoint + t * (farPoint - nearPoint);
    
    //gl_FragDepth = computeDepth(fragPos3D);
    gl_FragDepth = ((gl_DepthRange.diff * computeDepth(fragPos3D)) + gl_DepthRange.near + gl_DepthRange.far) / 2.0;

    float linearDepth = computeLinearDepth(fragPos3D);
    float fading = max(0, (0.5 - linearDepth));

    outputColor = (grid(fragPos3D, 60, false) + grid(fragPos3D, 15, true)) * float(t > 0);
    outputColor.a *= fading;
}
