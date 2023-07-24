#version 460

//Includes - resolved by VRF
#include "compression.incl"
#include "animation.incl"
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
    mat4 skinTransform = transform * getSkinMatrix();
    vec4 fragPosition = skinTransform * vec4(vPOSITION, 1.0);
    gl_Position = uProjectionViewMatrix * fragPosition;
    vFragPosition = fragPosition.xyz / fragPosition.w;

    mat3 normalTransform = transpose(inverse(mat3(skinTransform)));

    //Unpack normals
#if (D_COMPRESSED_NORMALS_AND_TANGENTS == 0)
    vNormalOut = normalize(normalTransform * vNORMAL.xyz);
    vTangentOut = normalize(normalTransform * vTANGENT.xyz);
    vBitangentOut = cross(vNormalOut, vTangentOut);
#else
    vec4 tangent = DecompressTangent(vNORMAL);
    vNormalOut = normalize(normalTransform * DecompressNormal(vNORMAL));
    vTangentOut = normalize(normalTransform * tangent.xyz);
    vBitangentOut = tangent.w * cross( vNormalOut, vTangentOut );
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
