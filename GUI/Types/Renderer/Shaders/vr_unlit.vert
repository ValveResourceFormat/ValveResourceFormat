#version 330

in vec3 vPOSITION;
in vec2 vTEXCOORD;

out vec3 vFragPosition;

out vec2 vTexCoordOut;

uniform mat4 projection;
uniform mat4 modelview;
uniform mat4 transform;

void main()
{
	gl_Position = projection * modelview * transform * vec4(vPOSITION, 1.0);
	vFragPosition = vPOSITION;

	vTexCoordOut = vTEXCOORD;
}
