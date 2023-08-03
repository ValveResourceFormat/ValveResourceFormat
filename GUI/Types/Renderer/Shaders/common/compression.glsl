// Utility file containing utility function to deal with compressed data from shaders.

#define D_COMPRESSED_NORMALS_AND_TANGENTS 0

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

void DecompressNormalAndTangents2(uint nPackedFrame, out vec3 normal, out vec4 tangent)
{
    // TODO: Cleanup
    float nPackedFrameX = fma(float((nPackedFrame >> 12u) & 1023u), 0.00195503421127796173095703125, -1.0);
    float nPackedFrameY = fma(float((nPackedFrame >> 22u) & 1023u), 0.00195503421127796173095703125, -1.0);
    float _23404 = (1.0 - abs(nPackedFrameX)) - abs(nPackedFrameY);
    vec3 _8254 = vec3(nPackedFrameX, nPackedFrameY, _23404);
    float _24401 = clamp(-_23404, 0.0, 1.0);
    vec2 _15528 = _8254.xy;
    vec2 _13433 = _15528 + mix(vec2(_24401), vec2(-_24401), greaterThanEqual(_15528, vec2(0.0)));
    vec3 normalY = normalize(vec3(_13433.x, _13433.y, _8254.z));
    float normalZ = normalY.z;
    float _8220 = (normalZ >= 0.0) ? 1.0 : (-1.0);
    float _16417 = (-1.0) / (_8220 + normalZ);
    float normalX = normalY.x;
    vec3 _23176 = vec3(fma((_8220 * normalX) * normalX, _16417, 1.0), _8220 * ((normalX * normalY.y) * _16417), (-_8220) * normalX);
    float nPackedFrameZ = float((nPackedFrame >> 1u) & 2047u) * 0.003069460391998291015625;

    normal = vec3(normalX, normalY.y, normalZ);
    tangent = vec4((_23176 * cos(nPackedFrameZ)) + (cross(normalY, _23176) * sin(nPackedFrameZ)), 0.0);
    tangent.w = ((nPackedFrame & 1u) == 0u) ? (-1.0) : 1.0;
}
