#version 460

in vec2 vPOSITION;
in vec2 vTEXCOORD;
in vec4 vCOLOR;
in float vDEPTH;

out vec2 vTexCoord;
out vec4 vFragColor;
out float vSceneDepth;

uniform mat4 transform;

void main(void) {
    gl_Position = transform * vec4(vPOSITION, 0, 1);
    vTexCoord = vTEXCOORD;
    vFragColor = vCOLOR;
    vSceneDepth = vDEPTH;
}
