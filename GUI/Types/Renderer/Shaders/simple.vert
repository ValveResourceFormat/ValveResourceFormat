#version 330

//Includes - resolved by VRF
#include "compression.incl"
//End of includes

//Parameter defines - These are default values and can be overwritten based on material/model parameters
#define param_fulltangent 1
//End of parameter defines

in vec3 vPosition;
in vec4 vNormal;
in vec2 vTexCoord;
in vec4 vTangent;
in ivec4 vBlendIndices;
in vec4 vBlendWeight;

out vec3 vFragPosition;

out vec3 vNormalOut;
out vec3 vTangentOut;
out vec3 vBitangentOut;

out vec2 vTexCoordOut;

uniform mat4 projection;
uniform mat4 modelview;
uniform mat4 transform;

void main()
{
	gl_Position = projection * modelview * transform * vec4(vPosition, 1.0);
	vFragPosition = vPosition;

	//Unpack normals
#if param_fulltangent == 1
	vNormalOut = vNormal.xyz;
#else
	vec4 tangent = DecompressTangent(vNormal);
	vNormalOut = DecompressNormal(vNormal);
#endif

	vTexCoordOut = vTexCoord;
}
