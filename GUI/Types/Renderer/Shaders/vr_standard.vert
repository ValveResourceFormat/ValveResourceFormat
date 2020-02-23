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
    mat4 skinTransformMatrix = transform * getSkinMatrix();
    vec4 fragPosition = skinTransformMatrix * vec4(vPOSITION, 1.0);
    gl_Position = projection * modelview * fragPosition;
    vFragPosition = fragPosition.xyz / fragPosition.w;

    // Calculate model matrix
    mat4 normalTransform = skinTransformMatrix;
    // Remove translation from matrix
    normalTransform[3][0] = 0.0;
    normalTransform[3][1] = 0.0;
    normalTransform[3][2] = 0.0;

	//Unpack normals
#if param_fulltangent == 1
    vNormalOut = normalize((normalTransform * vNORMAL).xyz);
    vTangentOut = normalize((normalTransform * vTANGENT).xyz);
    vBitangentOut = cross(vNormalOut, vTangentOut);
#else
    vec4 tangent = DecompressTangent(vNORMAL);
    vNormalOut = normalize((normalTransform * vec4(DecompressNormal(vNORMAL), 0.0)).xyz);
    vTangentOut = normalize((normalTransform * vec4(tangent.xyz, 0.0)).xyz);
    vBitangentOut = tangent.w * cross( vNormalOut, vTangentOut );
#endif

	vTexCoordOut = vTEXCOORD;
}
