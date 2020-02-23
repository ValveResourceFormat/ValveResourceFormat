#version 330

#include "animation.incl";

in vec3 vPOSITION;
in vec2 vTEXCOORD;

out vec3 vFragPosition;

out vec2 vTexCoordOut;

uniform mat4 projection;
uniform mat4 modelview;
uniform mat4 transform;

void main()
{
    vec4 fragPosition = transform * getSkinMatrix() * vec4(vPOSITION, 1.0);
	gl_Position = projection * modelview * fragPosition;
	vFragPosition = fragPosition.xyz / fragPosition.w;

	vTexCoordOut = vTEXCOORD;
}
