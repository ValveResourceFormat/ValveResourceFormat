#version 460

#include "common/utils.glsl"
#include "common/rendermodes.glsl"

in vec4 vtxColor;
in vec3 vtxNormal;
in vec3 vtxPos;
in vec3 camPos;

out vec4 outputColor;

float shadedStrength = 0.8;

void main(void) {
    if (vtxNormal == vec3(0, 0, 0)) {
        outputColor = vtxColor;
    } else {
        vec3 viewDir = normalize(vtxPos - camPos);

        vec3 shaded = CalculateFullbrightLighting(vtxColor.rgb, vtxNormal, viewDir);
        outputColor = vec4(mix(vtxColor.rgb, shaded, 0.8), vtxColor.a);
    }

    #if renderMode_Normals == 1
        outputColor = vec4(PackToColor(vtxNormal), 1.0);
    #endif

    #if renderMode_Color == 1
        outputColor = vec4(vtxColor.rgb, 1.0);
    #endif
}
