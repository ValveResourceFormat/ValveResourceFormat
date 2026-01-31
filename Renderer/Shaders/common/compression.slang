#version 460

extern static const int D_COMPRESSED_NORMALS_AND_TANGENTS = 0;



#include "utils.glSlang"

// File containing vertex inputs and functions to deal with compressed normals and tangents.


#if (D_COMPRESSED_NORMALS_AND_TANGENTS == 0 || D_COMPRESSED_NORMALS_AND_TANGENTS == 1)
    in vec4 vNORMAL; // OptionallyCompressedTangentFrame
    #if (D_COMPRESSED_NORMALS_AND_TANGENTS == 0)
        in vec4 vTANGENT; // TangentU_SignV
    #endif
#elif (D_COMPRESSED_NORMALS_AND_TANGENTS == 2)
    in uint vNORMAL; // CompressedTangentFrame
#endif

mat3 adjoint(in mat4 m)
{
    return mat3(cross(m[1].xyz, m[2].xyz),
                cross(m[2].xyz, m[0].xyz),
                cross(m[0].xyz, m[1].xyz));
}

//Decompress a byte4 normal in the 0..255 range to a float4 tangent
vec4 DecompressTangent( vec4 inputNormal )
{
    float fOne   = 1.0f;
    vec4 ztztSignBits	= -floor(( inputNormal - 128.0f )/127.0f);						// sign bits for zs and bitangent (1 or 0)  set-less-than (slt) asm instruction
    vec4 xyxyAbs		= abs( inputNormal - 128.0f ) - ztztSignBits;		// 0..127
    vec4 xyxySignBits	= -floor(( xyxyAbs - 64.0f )/63.0f);							// sign bits for xs and ys (1 or 0)
    vec4 normTan		= (abs( xyxyAbs - 64.0f ) - xyxySignBits) / 63.0f;	// abs({nX, nY, tX, tY})

    vec4 outputTangent;
    outputTangent.xy	= normTan.zw;										// abs({tX, tY, __, __})

    vec4 xyxySigns	= 1 - 2*xyxySignBits;								// Convert sign bits to signs
    vec4 ztztSigns	= 1 - 2*ztztSignBits;								// ( [1,0] -> [-1,+1] )

    outputTangent.z		= 1.0f - outputTangent.x - outputTangent.y;			// Project onto x+y+z=1
    outputTangent.xyz	= normalize( outputTangent.xyz );					// Normalize onto unit sphere
    outputTangent.xy   *= xyxySigns.zw;										// Restore x and y signs
    outputTangent.z	   *= ztztSigns.z;										// Restore z sign
    outputTangent.w		= ztztSigns.w;

    return outputTangent;
}

//Decompress a byte4 normal in the 0..255 range to a float3 normal
vec3 DecompressNormal( vec4 inputNormal )
{
    float fOne   = 1.0f;
    vec3 outputNormal = vec3(0);

    vec2 ztSigns      = -floor(( inputNormal.xy - 128.0f )/127.0f);      // sign bits for zs and bitangent (1 or 0)  set-less-than (slt) asm instruction
    vec2 xyAbs        = abs( inputNormal.xy - 128.0f ) - ztSigns;     // 0..127
    vec2 xySigns      = -floor(( xyAbs - 64.0f )/63.0f);             // sign bits for xs and ys (1 or 0)
    outputNormal.xy     = ( abs( xyAbs - 64.0f ) - xySigns ) / 63.0f;   // abs({nX, nY})

    outputNormal.z      = 1.0f - outputNormal.x - outputNormal.y;       // Project onto x+y+z=1
    outputNormal.xyz    = normalize( outputNormal.xyz );                // Normalize onto unit sphere

    outputNormal.xy    *= mix( vec2(fOne,fOne), vec2(-fOne, -fOne), xySigns   );                // Restore x and y signs
    outputNormal.z     *= mix( fOne, -fOne, ztSigns.x );                // Restore z sign

    return normalize(outputNormal);
}

// Decompress a uint into a float3 normal and float4 tangent. Added in CS2
void DecompressNormalTangent2(uint nPackedFrame, out vec3 normal, out vec4 tangent)
{
    uint SignBit = nPackedFrame & 1u;            // LSB bit
    float Tbits = (nPackedFrame >> 1u) & 0x7ff;  // 11 bits
    float Xbits = (nPackedFrame >> 12u) & 0x3ff; // 10 bits
    float Ybits = (nPackedFrame >> 22u) & 0x3ff; // 10 bits

    // Unpack from 0..1 to -1..1
    float nPackedFrameX = (Xbits / 1023.0f) * 2.0 - 1.0;
    float nPackedFrameY = (Ybits / 1023.0f) * 2.0 - 1.0;

    // Z is never given a sign, meaning negative values are caused by abs(packedframexy) adding up to over 1.0
    float derivedNormalZ = 1.0 - abs(nPackedFrameX) - abs(nPackedFrameY); // Project onto x+y+z=1
    vec3 unpackedNormal = vec3( nPackedFrameX, nPackedFrameY, derivedNormalZ );

    // If Z is negative, X and Y has had extra amounts (TODO: find the logic behind this value) added into them so they would add up to over 1.0
    // Thus, we take the negative components of Z and add them back into XY to get the correct original values.
    vec2 negativeZCompensation = vec2( saturate(-derivedNormalZ) ); // Isolate the negative 0..1 range of derived Z
    unpackedNormal.xy += mix(negativeZCompensation, -negativeZCompensation, greaterThanEqual(unpackedNormal.xy, vec2(0.0)));

    normal = normalize(unpackedNormal); // Get final normal by normalizing it onto the unit sphere

    // Invert tangent when normal Z is negative
    float tangentSign = (normal.z >= 0.0) ? 1.0 : -1.0;
    // equal to tangentSign * (1.0 + abs(normal.z))
    float rcpTangentZ = 1.0 / (tangentSign + normal.z);

    // Be careful of rearranging ops here, could lead to differences in float precision, especially when dealing with compressed data.
    vec3 unalignedTangent;

    // Unoptimized (but clean) form:
    // tangent.X = -(normal.x * normal.x) / (tangentSign + normal.z) + 1.0
    // tangent.Y = -(normal.x * normal.y) / (tangentSign + normal.z)
    // tangent.Z = -(normal.x)
    unalignedTangent.x = -tangentSign * (normal.x * normal.x) * rcpTangentZ + 1.0; // ???
    unalignedTangent.y = -tangentSign * ((normal.x * normal.y) * rcpTangentZ);
    unalignedTangent.z = -tangentSign * normal.x;

    // This establishes a single direction on the tangent plane that derived from only the normal (has no texcoord info).
    // But it doesn't line up with the texcoords. For that, it uses nPackedFrameT, which is the rotation.

    // Angle to use to rotate tangent
    float nPackedFrameT = (Tbits / 2047.0f) * TAU;

    // Rotate tangent to the correct angle that aligns with texcoords.
    tangent.xyz = unalignedTangent * cos(nPackedFrameT) + cross(normal, unalignedTangent) * sin(nPackedFrameT);

    tangent.w = (SignBit == 0u) ? -1.0 : 1.0; // Bitangent sign bit... inverted (0 = negative)
}

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


