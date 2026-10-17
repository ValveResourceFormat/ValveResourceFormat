// File containing vertex inputs and functions to deal with compressed normals and tangents.
import Core;
import utils;

[ConstantRange(0, 2)]
extern static const int D_COMPRESSED_NORMALS_AND_TANGENTS = 0;

public struct NormalData
{
    private Conditional < float4, D_COMPRESSED_NORMALS_AND_TANGENTS < 2> optionallyCompressedNormal : NORMAL;
    private Conditional < float4, D_COMPRESSED_NORMALS_AND_TANGENTS == 0> rawTangent : TANGENT;
    private Conditional<uint, D_COMPRESSED_NORMALS_AND_TANGENTS == 2> m_packed : NORMAL;

    public property float3 normal
    {
        get
        {
            if (D_COMPRESSED_NORMALS_AND_TANGENTS == 0)
            {
                return optionallyCompressedNormal.get().value.xyz;
            }
            else if (D_COMPRESSED_NORMALS_AND_TANGENTS == 1)
            {
                return DecompressNormal(optionallyCompressedNormal.get().value * 255.f);
            }
            else
            {
                float3 normal;
                float4 tangent;
                DecompressNormalTangent2(m_packed.get().value, normal, tangent);
                return normal;
            };
        }
    }
    public property float3 tangent
    {
        get
        {
            if (D_COMPRESSED_NORMALS_AND_TANGENTS == 0)
            {
                return rawTangent.get().value.xyz;
            }
            else if (D_COMPRESSED_NORMALS_AND_TANGENTS == 1)
            {
                return DecompressTangent(optionallyCompressedNormal.get().value * 255.f).xyz;
            }
            else {
                float3 normal;
                float4 tangent;
                DecompressNormalTangent2(m_packed.get().value, normal, tangent);
                return tangent.xyz;
            };
        }
    }
    public property float3 bitangent
    {
        get
        {
            if (D_COMPRESSED_NORMALS_AND_TANGENTS == 0)
            {
                return cross(optionallyCompressedNormal.get().value.xyz, tangent) * rawTangent.get().value.w;
            }
            else if (D_COMPRESSED_NORMALS_AND_TANGENTS == 1)
            {
                var decompNormal = DecompressNormal(optionallyCompressedNormal.get().value * 255.f);
                var decompTangent = DecompressTangent(optionallyCompressedNormal.get().value * 255.f);
                return cross(decompNormal, decompTangent.xyz) * decompTangent.w;
            }
            else {
                float3 normal;
                float4 tangent;
                DecompressNormalTangent2(m_packed.get().value, normal, tangent);
                return cross(normal, tangent.xyz) * tangent.w;
            };
        }
    }
}

// Decompress a byte4 normal in the 0..255 range to a float4 tangent
float4 DecompressTangent( float4 inputNormal )
{
    float fOne = 1.0f;
    float4 ztztSignBits = float4(inputNormal < float4(128.0f)); // sign bits for zs and bitangent (1 or 0)  set-less-than (slt) asm instruction
    float4 xyxyAbs = abs(inputNormal - 128.0f) - ztztSignBits;        // 0..127
    float4 xyxySignBits = float4(xyxyAbs < float4(64.0f));       // sign bits for xs and ys (1 or 0)
    float4 normTan		= (abs( xyxyAbs - 64.0f ) - xyxySignBits) / 63.0f;	// abs({nX, nY, tX, tY})

    float4 outputTangent;
    outputTangent.xy	= normTan.zw;										// abs({tX, tY, __, __})

    float4 xyxySigns = 1 - 2 * xyxySignBits; // Convert sign bits to signs
    float4 ztztSigns	= 1 - 2*ztztSignBits;								// ( [1,0] -> [-1,+1] )

    outputTangent.z		= 1.0f - outputTangent.x - outputTangent.y;			// Project onto x+y+z=1
    outputTangent.xyz	= normalize( outputTangent.xyz );					// Normalize onto unit sphere
    outputTangent.xy   *= xyxySigns.zw;										// Restore x and y signs
    outputTangent.z	   *= ztztSigns.z;										// Restore z sign
    outputTangent.w		= ztztSigns.w;

    return outputTangent;
}

// Decompress a byte4 normal in the 0..255 range to a float3 normal
float3 DecompressNormal(float4 inputNormal )
{
    float fOne = 1.0f;
    float3 outputNormal = float3(0);

    float2 ztSigns = float2(inputNormal.xy < float2(128.0f)); // sign bits for zs and bitangent (1 or 0)  set-less-than (slt) asm instruction
    float2 xyAbs = abs(inputNormal.xy - 128.0f) - ztSigns;          // 0..127
    float2 xySigns      = float2(xyAbs < float2(64.0f));             // sign bits for xs and ys (1 or 0)
    outputNormal.xy     = ( abs( xyAbs - 64.0f ) - xySigns ) / 63.0f;   // abs({nX, nY})

    outputNormal.z      = 1.0f - outputNormal.x - outputNormal.y;       // Project onto x+y+z=1
    outputNormal.xyz    = normalize( outputNormal.xyz );                // Normalize onto unit sphere

    outputNormal.xy *= lerp(float2(fOne, fOne), float2(-fOne, -fOne), xySigns); // Restore x and y signs
    outputNormal.z *= lerp( fOne, -fOne, ztSigns.x );                // Restore z sign

    return normalize(outputNormal);
}

void DecompressNormalTangent2(
    uint nPackedFrame,
    out float3 normal,
    out float4 tangent)
{
    uint signBit = nPackedFrame & 1u;
    float tBits = float((nPackedFrame >> 1u) & 0x7FFu);
    float xBits = float((nPackedFrame >> 12u) & 0x3FFu);
    float yBits = float((nPackedFrame >> 22u) & 0x3FFu);

    // Unpack from 0..1 to -1..1
    float nPackedFrameX = (xBits / 1023.0) * 2.0 - 1.0;
    float nPackedFrameY = (yBits / 1023.0) * 2.0 - 1.0;

    // Z is never given a sign. Negative Z is reconstructed from XY.
    float derivedNormalZ =
        1.0 - abs(nPackedFrameX) - abs(nPackedFrameY);

    float3 unpackedNormal = float3(
        nPackedFrameX,
        nPackedFrameY,
        derivedNormalZ);

    // Compensate XY when the derived Z is negative.
    float negativeZCompensation = saturate(-derivedNormalZ);

    unpackedNormal.xy += select(
        unpackedNormal.xy >= float2(0.0),
        float2(-negativeZCompensation),
        float2(negativeZCompensation));

    normal = normalize(unpackedNormal);

    // Invert tangent when normal Z is negative.
    float tangentSign = normal.z >= 0.0 ? 1.0 : -1.0;

    // Equal to:
    // 1.0 / (tangentSign + normal.z)
    float rcpTangentZ = 1.0 / (tangentSign + normal.z);

    float3 unalignedTangent;

    // Keep these operations in this order.
    unalignedTangent.x =
        -tangentSign * (normal.x * normal.x) * rcpTangentZ + 1.0;

    unalignedTangent.y =
        -tangentSign * ((normal.x * normal.y) * rcpTangentZ);

    unalignedTangent.z =
        -tangentSign * normal.x;

    // Tangent rotation.
    float nPackedFrameT =
        (tBits / 2047.0) * TAU;

    float cosT = cos(nPackedFrameT);
    float sinT = sin(nPackedFrameT);

    tangent.xyz =
        unalignedTangent * cosT +
        cross(normal, unalignedTangent) * sinT;

    // Bitangent sign bit is inverted: 0 = negative.
    tangent.w = signBit == 0u ? -1.0 : 1.0;
}

//void GetOptionallyCompressedNormalTangent(out float3 normal, out float4 tangent)
//{
//    #if (D_COMPRESSED_NORMALS_AND_TANGENTS == 0)
//        normal = vNORMAL.xyz;
//        tangent = vTANGENT.xyzw;
//    #elif (D_COMPRESSED_NORMALS_AND_TANGENTS == 1)
//        normal = DecompressNormal(vNORMAL * 255.0);
//        tangent = DecompressTangent(vNORMAL * 255.0);
//    #elif (D_COMPRESSED_NORMALS_AND_TANGENTS == 2)
//        DecompressNormalTangent2(vNORMAL, normal, tangent);
//    #endif
//}
