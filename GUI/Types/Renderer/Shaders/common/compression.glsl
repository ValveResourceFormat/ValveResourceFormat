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

//Decompress a byte4 normal in the 0..255 range to a float4 tangent
vec4 DecompressTangent( vec4 inputNormal )
{
    float fOne   = 1.0f;
    vec4 ztztSignBits	= -floor(( inputNormal - 128.0f )/127.0f);						// sign bits for zs and binormal (1 or 0)  set-less-than (slt) asm instruction
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

    vec2 ztSigns      = -floor(( inputNormal.xy - 128.0f )/127.0f);      // sign bits for zs and binormal (1 or 0)  set-less-than (slt) asm instruction
    vec2 xyAbs        = abs( inputNormal.xy - 128.0f ) - ztSigns;     // 0..127
    vec2 xySigns      = -floor(( xyAbs - 64.0f )/63.0f);             // sign bits for xs and ys (1 or 0)
    outputNormal.xy     = ( abs( xyAbs - 64.0f ) - xySigns ) / 63.0f;   // abs({nX, nY})

    outputNormal.z      = 1.0f - outputNormal.x - outputNormal.y;       // Project onto x+y+z=1
    outputNormal.xyz    = normalize( outputNormal.xyz );                // Normalize onto unit sphere

    outputNormal.xy    *= mix( vec2(fOne,fOne), vec2(-fOne, -fOne), xySigns   );                // Restore x and y signs
    outputNormal.z     *= mix( fOne, -fOne, ztSigns.x );                // Restore z sign

    return normalize(outputNormal);
}

void DecompressNormalTangent2(uint nPackedFrame, out vec3 normal, out vec4 tangent)
{
    const float fMagicN = 0.00195503421127796173095703125;  // ~ 1.0 / 512.0
    const float fMagicT = 0.003069460391998291015625;       // ~ 1.0 / 326.0

    uint SignBit = nPackedFrame & 1u;           // LSB bit
    float Zbits = (nPackedFrame >> 1u) & 0x7ff;  // 11 bits
    float Xbits = (nPackedFrame >> 12u) & 0x3ff; // 10 bits
    float Ybits = (nPackedFrame >> 22u) & 0x3ff; // 10 bits

    float nPackedFrameX = fma(Xbits, fMagicN, -1.0);
    float nPackedFrameY = fma(Ybits, fMagicN, -1.0);

    float _23404 = (1.0 - abs(nPackedFrameX)) - abs(nPackedFrameY);
    vec3 _8254 = vec3(nPackedFrameX, nPackedFrameY, _23404);
    float _24401 = clamp(-_23404, 0.0, 1.0);
    vec2 _15528 = _8254.xy;
    vec2 _13433 = _15528 + mix(vec2(_24401), vec2(-_24401), greaterThanEqual(_15528, vec2(0.0)));
    normal = normalize(vec3(_13433.x, _13433.y, _8254.z));
  
    float _8220 = (normal.z >= 0.0) ? 1.0 : (-1.0);
    float _16417 = (-1.0) / (_8220 + normal.z);
    vec3 _23176 = vec3(fma((_8220 * normal.x) * normal.x, _16417, 1.0), _8220 * ((normal.x * normal.y) * _16417), (-_8220) * normal.x);

    float nPackedFrameZ = Zbits * fMagicT;
    
    tangent.xyz = _23176 * cos(nPackedFrameZ) + cross(normal, _23176) * sin(nPackedFrameZ);
    tangent.w = SignBit == 0u ? -1.0 : 1.0;
}

void GetOptionallyCompressedNormalTangent(out vec3 normal, out vec4 tangent)
{
    #if (D_COMPRESSED_NORMALS_AND_TANGENTS == 0)
        normal = vNORMAL.xyz;
        tangent = vTANGENT.xyzw;
    #elif (D_COMPRESSED_NORMALS_AND_TANGENTS == 1)
        normal = DecompressNormal(vNORMAL);
        tangent = DecompressTangent(vNORMAL);
    #elif (D_COMPRESSED_NORMALS_AND_TANGENTS == 2)
        DecompressNormalTangent2(vNORMAL, normal, tangent);
    #endif
}
