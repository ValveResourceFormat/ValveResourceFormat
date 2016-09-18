#version 330

//Includes - resolved by VRF
#include "compression.incl"
//End of includes

//Parameter defines - These are default values and can be overwritten based on material/model parameters
#define param_fulltangent 1
//End of parameter defines

in vec3 vPOSITION;
in vec4 vNORMAL;
in vec2 vTEXCOORD;
in vec4 vTANGENT;
in vec4 vBLENDINDICES;
in vec4 vBLENDWEIGHT;

out vec3 vFragPosition;

out vec3 vNormalOut;
out vec3 vTangentOut;
out vec3 vBitangentOut;
out float fTangentW;

out vec2 vTexCoordOut;

uniform mat4 projection;
uniform mat4 modelview;
uniform mat4 transform;

uniform float bAnimated = 0;
uniform float fNumBones = 1;
uniform sampler2D animationTexture;

mat4 getMatrix(float id) {
    float texelPos = id/fNumBones;
    return mat4(texture2D(animationTexture, vec2(0.00, texelPos)),
        texture2D(animationTexture, vec2(0.25, texelPos)),
        texture2D(animationTexture, vec2(0.50, texelPos)),
        texture2D(animationTexture, vec2(0.75, texelPos)));
}

mat4 getSkinMatrix() {
    mat4 matrix;
    matrix += vBLENDWEIGHT.x * getMatrix(vBLENDINDICES.x);
    matrix += vBLENDWEIGHT.y * getMatrix(vBLENDINDICES.y);
    matrix += vBLENDWEIGHT.z * getMatrix(vBLENDINDICES.z);
    return bAnimated * matrix + (1 - bAnimated) * mat4(1.0);
}

mat3 getNormalMat(mat4 mat) {
    return mat3(mat[0][0], mat[1][0], mat[2][0], mat[0][1], mat[1][1], mat[2][1], mat[0][2], mat[1][2], mat[2][2]);
}

void main()
{
	mat4 skinMatrix = getSkinMatrix();

    gl_Position = projection * modelview * transform * skinMatrix * vec4(vPOSITION, 1.0);
	vFragPosition = vPOSITION;

	//Unpack normals
#if param_fulltangent == 1
	vNormalOut = vNORMAL.xyz;
	vTangentOut = vTANGENT.xyz;
	vBitangentOut = cross( vNormalOut, vTangentOut );
#else
	vec3 tangent = DecompressTangent(vNORMAL);
	vNormalOut = DecompressNormal(vNORMAL);
	vTangentOut = tangent;
	vBitangentOut = cross( vNormalOut, vTangentOut );
#endif

	vTexCoordOut = vTEXCOORD;
}
