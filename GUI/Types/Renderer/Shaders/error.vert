#version 330

in vec3 vPOSITION;
in vec2 vTEXCOORD;

out vec2 vTexCoordOut;

uniform mat4 uProjectionViewMatrix;

void main(void) {
    vTexCoordOut = vTEXCOORD;
    gl_Position = uProjectionViewMatrix * vec4(vPOSITION, 1.0);
}
