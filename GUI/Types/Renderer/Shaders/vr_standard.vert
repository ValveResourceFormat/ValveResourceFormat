#version 330

//Includes - resolved by VRF
#include "compression.incl";
#include "animation.incl";
//End of includes

//Parameter defines - These are default values and can be overwritten based on material/model parameters
#define param_fulltangent 1
//End of parameter defines

in vec3 vPOSITION;
in vec4 vNORMAL;
in vec2 vTEXCOORD;
in vec4 vTANGENT;

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
    vec4 fragPosition = transform * getSkinMatrix() * vec4(vPOSITION, 1.0);
	gl_Position = projection * modelview * fragPosition;
	vFragPosition = fragPosition.xyz;

	//Unpack normals
#if param_fulltangent == 1
	vNormalOut = vNORMAL.xyz;
	vTangentOut = vTANGENT.xyz;
	vBitangentOut = cross( vNormalOut, vTangentOut );
#else
	vec3 tangent = DecompressTangent(vNORMAL);
	vNormalOut = DecompressNormal(vNORMAL);
	vTangentOut = -tangent.xyz;
	vBitangentOut = cross( vNormalOut, vTangentOut );
#endif

	vTexCoordOut = vTEXCOORD;
}
