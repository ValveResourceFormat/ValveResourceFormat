#version 460

#ifndef VRF_COMPRESSION_INCLUDE
#define VRF_COMPRESSION_INCLUDE

#include "utils.slang"
#include "normal_decode.slang"

// File containing vertex inputs and functions to deal with compressed normals and tangents.

#define D_COMPRESSED_NORMALS_AND_TANGENTS 0

#if (D_COMPRESSED_NORMALS_AND_TANGENTS == 0 || D_COMPRESSED_NORMALS_AND_TANGENTS == 1)
    in vec4 vNORMAL; // OptionallyCompressedTangentFrame
    #if (D_COMPRESSED_NORMALS_AND_TANGENTS == 0)
        in vec4 vTANGENT; // TangentU_SignV
    #endif
#elif (D_COMPRESSED_NORMALS_AND_TANGENTS == 2)
    in uint vNORMAL; // CompressedTangentFrame
#endif

void GetOptionallyCompressedNormalTangent(out vec3 normal, out vec4 tangent)
{
    #if (D_COMPRESSED_NORMALS_AND_TANGENTS == 0)
        normal = vNORMAL.xyz;
        tangent = vTANGENT.xyzw;
    #elif (D_COMPRESSED_NORMALS_AND_TANGENTS == 1)
        normal = DecompressNormal(vNORMAL * 255.0);
        tangent = DecompressTangent(vNORMAL * 255.0);
    #elif (D_COMPRESSED_NORMALS_AND_TANGENTS == 2)
        DecompressNormalTangent2(vNORMAL, normal, tangent);
    #endif
}

#endif
