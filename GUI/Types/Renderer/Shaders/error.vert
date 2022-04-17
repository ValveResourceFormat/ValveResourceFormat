#version 330

//Includes - resolved by VRF
#include "animation.incl"
//End of includes

in vec3 vPOSITION;
in vec2 vTEXCOORD;

out vec2 vTexCoordOut;

uniform mat4 uProjectionViewMatrix;
uniform mat4 transform;

void main(void) {
    vTexCoordOut = vTEXCOORD;
    gl_Position = uProjectionViewMatrix * transform * getSkinMatrix() * vec4(vPOSITION, 1.0);
}
