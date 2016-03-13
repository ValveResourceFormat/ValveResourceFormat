#version 330

in vec3 vPosition;
in vec3 vNormal;
in vec2 vTexCoord;
in vec4 vTangent;
in ivec4 vBlendIndices;
in vec4 vBlendWeight;

out vec3 vNormalOut;
out vec2 vTexCoordOut;

uniform mat4 projection;
uniform mat4 modelview;

void main()
{
	gl_Position = projection * modelview * vec4(vPosition, 1.0);
	vTexCoordOut = vTexCoord;
}
