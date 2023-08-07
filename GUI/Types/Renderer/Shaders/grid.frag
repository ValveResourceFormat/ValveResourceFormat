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

float computeDepth(vec4 clip_space_pos) {
    return (clip_space_pos.z / clip_space_pos.w);
}

float computeLinearDepth(vec4 clip_space_pos) {
    float clip_space_depth = (clip_space_pos.z / clip_space_pos.w) * 2.0 - 1.0; // put back between -1 and 1
    float linearDepth = (2.0 * near * far) / (far + near - clip_space_depth * (far - near)); // get linear value between 0.01 and 100
    return linearDepth / far; // normalize
}

void main() {
    float t = -nearPoint.z / (farPoint.z - nearPoint.z);

    vec3 fragPos3D = nearPoint + t * (farPoint - nearPoint);
    vec2 fragPosAbs = abs(fragPos3D.xy);
    vec4 clip_space_pos = g_matWorldToProjection * g_matWorldToView * vec4(fragPos3D.xyz, 1.0);
    
    gl_FragDepth = ((gl_DepthRange.diff * computeDepth(clip_space_pos)) + gl_DepthRange.near + gl_DepthRange.far) / 2.0;

    float linearDepth = computeLinearDepth(clip_space_pos);
    float fading = max(0, (0.5 - linearDepth));
    float scale = lessThanEqual(fragPosAbs, vec2(120.0)) == true ? 15f : 60f;

    vec2 coord = fragPos3D.xy / scale;
    vec2 derivative = fwidth(coord);
    vec2 grid = abs(fract(coord - 0.5) - 0.5) / derivative;
    float line = min(grid.x, grid.y);
    vec4 gridColor = vec4(0.7, 0.7, 1.0, 1.0 - min(line, 1.0));
    
    if (fragPosAbs.x < 1) {
        gridColor.xyz = vec3(0.7, 0.2, 0.2);
        gridColor.a *= fading * 2;
    } else if (fragPosAbs.y < 1) {
        gridColor.xyz = vec3(0.2, 0.7, 0.2);
        gridColor.a *= fading * 2;
    } else {
        gridColor *= fading;
    }

    outputColor = gridColor * float(t > 0);
}
