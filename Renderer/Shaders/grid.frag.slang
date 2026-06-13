#version 460

// https://github.com/BabylonJS/Babylon.js/blob/bd7351cfc97884d3293d5858b8a0190cda640b2f/packages/dev/materials/src/grid/grid.fragment.fx
// https://asliceofrendering.com/scene%20helper/2020/01/05/InfiniteGrid/

float near = 0.01;
float far = 100;
vec3 colorRed = vec3(0.9, 0.2, 0.2);
vec3 colorGreen = vec3(0.2, 0.8, 0.2);

sample in vec3 nearPoint;
sample in vec3 farPoint;

out vec4 outputColor;

#include "common/ViewConstants.slang"

float computeDepth(vec4 clip_space_pos) {
    return (clip_space_pos.z / clip_space_pos.w);
}

void main() {
    float t = -nearPoint.z / (farPoint.z - nearPoint.z);
    vec3 fragPos3D = nearPoint + t * (farPoint - nearPoint);
    vec2 fragPosAbs = abs(fragPos3D.xy);
    vec4 clip_space_pos = g_matViewToProjection * g_matWorldToView * vec4(fragPos3D.xyz, 1.0);
    float linearDepth = computeDepth(clip_space_pos);

    gl_FragDepth = ((gl_DepthRange.diff * linearDepth) + gl_DepthRange.near);

    float fading = max(0, (0.05 + pow(linearDepth, 0.1)));
    bool bIsNearOrigin = lessThanEqual(fragPosAbs, vec2(120.0)) == bvec2(1.0);

    float scale = bIsNearOrigin ? 15.0 : 60.0;
    vec2 coord = fragPos3D.xy / scale;
    vec2 derivative = fwidth(fragPos3D.xy)  / scale;

    vec2 grid = abs(fract(coord - 0.5) - 0.5) / derivative.xy;
    float line = min(grid.x, grid.y);
    vec4 gridColor = vec4(vec3(0.9, 0.9, 0.95), 1.0 - min(line, 1.0));

    float angleFade = min(1.0, pow(abs(normalize(fragPos3D - g_vCameraPositionWs).z), 1.2) * 20); // arbitrary values
    angleFade = mix(1.0, angleFade, min(length(fragPosAbs), 1.0));

    vec2 axisLines = abs(coord) / derivative;

    if (axisLines.x < 1) {
        float axisLineAlpha = (1 - min(axisLines.x, 1.0));
        gridColor.a = 1 - (1 - axisLineAlpha) * (min(grid.y, 1.0));
        gridColor.xyz = gridColor.xyz * (1 - axisLineAlpha) * (1 - min(grid.y, 1.0)) + colorGreen * axisLineAlpha; // color = A.rgb * A.a *  (1 - B.a) + B.rgb * B.a
        gridColor.xyz /= gridColor.a; // normalize color to full alpha
        gridColor.a *= 2 - (1 - min(grid.y, 1.0)) / (2 - axisLines.x - min(grid.y, 1.0)); // mix alpha multiplier
    }

    if (axisLines.y < 1) {
        float axisLineAlpha = (1 - min(axisLines.y, 1.0));
        float crossAxisLineAlpha = 1 - min(grid.x, 1.0);

        if (min(axisLines.x, 1.0) == 1) {
            gridColor.a = 1 - (1 - axisLineAlpha) * (1 - crossAxisLineAlpha);
            gridColor.xyz = gridColor.xyz * (1 - axisLineAlpha) * crossAxisLineAlpha + colorRed * axisLineAlpha;
            gridColor.xyz /= gridColor.a;
            gridColor.a *= mix(2 , 1, crossAxisLineAlpha / (axisLineAlpha + crossAxisLineAlpha));
        } else { // for blending where the two lines meet
            gridColor.xyz = mix(colorGreen, colorRed, axisLineAlpha / (axisLineAlpha + crossAxisLineAlpha));
            gridColor.a   = max(axisLineAlpha, crossAxisLineAlpha) * 2;
        }
    }

    if (bIsNearOrigin)
    {
        gridColor.xyz *= 1.2;
        fading *= 1.4;
    }

    gridColor.a *= fading * angleFade;
    outputColor = gridColor *  float(t > 0);

    // output to sRGB space
    outputColor.rgb = pow(outputColor.rgb, vec3(2.2));
}
