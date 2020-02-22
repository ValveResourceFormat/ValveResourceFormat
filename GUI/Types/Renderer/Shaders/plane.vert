#version 330

in vec3 vPOSITION;
in vec3 vNORMAL;
in vec2 vTEXCOORD;
in vec3 vTANGENT;

out vec2 vPOSITIONOut;
out vec2 vTexCoordOut;
out vec3 vNormalOut;
out vec3 vTangentOut;

void main(void) {
    gl_Position = vec4(vPOSITION, 1.0);
    vTexCoordOut = vTEXCOORD;
    vNormalOut = vNORMAL;
    vTangentOut = vTANGENT;
}
