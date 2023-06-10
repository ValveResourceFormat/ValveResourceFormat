#version 330

//Includes - resolved by VRF
#include "compression.incl"
//End of includes

layout (location = 0) in vec3 vPOSITION;
in vec4 vNORMAL;
in vec2 vTEXCOORD;
in vec4 vTEXCOORD1;
in vec4 vTEXCOORD2;
in vec4 vTEXCOORD3;
#if (D_COMPRESSED_NORMALS_AND_TANGENTS == 0)
    in vec4 vTANGENT;
#endif
in ivec4 vBLENDINDICES;
in vec4 vBLENDWEIGHT;

out vec3 vFragPosition;

out vec3 vNormalOut;
out vec3 vTangentOut;
out vec3 vBitangentOut;

out vec4 vBlendWeights;
out vec4 vWeightsOut1;
out vec4 vWeightsOut2;

out vec2 vTexCoordOut;

uniform mat4 uProjectionViewMatrix;
uniform mat4 transform;

void main()
{
    vec4 fragPosition = transform * vec4(vPOSITION, 1.0);
    gl_Position = uProjectionViewMatrix * fragPosition;
    vFragPosition = fragPosition.xyz / fragPosition.w;

    //Unpack normals
#if (D_COMPRESSED_NORMALS_AND_TANGENTS == 0)
    vNormalOut = vNORMAL.xyz;
    vBitangentOut = vTANGENT.xyz;
#else
    vNormalOut = DecompressNormal(vNORMAL);
#endif

    vTexCoordOut = vTEXCOORD;

    //Normalize (?)
    //vTEXCOORD1 - seems empty
    //vTEXCOORD2 - (X,Y,Z) - Ambient occlusion, W - ???
    //vTEXCOORD3 - X - amount of tex1, Y - amount of tex2, Z - amount of tex3, W - ???
    vBlendWeights = vTEXCOORD3/255.0;
    vWeightsOut1 = vTEXCOORD1;
    vWeightsOut2 = vTEXCOORD2/255.0;
}
