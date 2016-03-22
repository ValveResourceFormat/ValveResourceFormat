#version 330

//Parameter definitions
#define param_fulltangent 0
//End of parameter definitions

in vec3 vPosition;
in vec4 vNormal;
in vec2 vTexCoord;
in vec4 vTangent;
in ivec4 vBlendIndices;
in vec4 vBlendWeight;

out vec3 vFragPosition;
out vec3 vNormalOut;
out vec4 vTangentOut;
out vec2 vTexCoordOut;

uniform mat4 projection;
uniform mat4 modelview;

//Decompress a byte4 normal in the 0..255 range to a float4 tangent
vec4 DecompressTangent( vec4 inputNormal )
{
    float fOne   = 1.0f;
    vec4 outputTangent = vec4(0);

    vec4 ztztSignBits = -floor(( inputNormal - 128.0f )/127.0f);       // sign bits for zs and binormal (1 or 0)  set-less-than (slt) asm instruction
    vec4 xyxyAbs      = abs( inputNormal - 128.0f ) - ztztSignBits;       // 0..127
    vec4 xyxySignBits = -floor(( xyxyAbs - 64.0f )/63.0f);                  // sign bits for xs and ys (1 or 0)
    vec4 normTan      = (abs( xyxyAbs - 64.0f ) - xyxySignBits) / 63.0f;  // abs({nX, nY, tX, tY})
    outputTangent.xy    = normTan.zw;                                       // abs({tX, tY, __, __})

    vec4 xyxySigns    = 1 - 2*xyxySignBits;                               // Convert sign bits to signs
    vec4 ztztSigns    = 1 - 2*ztztSignBits;                               // ( [1,0] -> [-1,+1] )

    outputTangent.z     = 1.0f - outputTangent.x - outputTangent.y;         // Project onto x+y+z=1
    outputTangent.xyz   = normalize( outputTangent.xyz );                   // Normalize onto unit sphere
    outputTangent.xy   *= xyxySigns.zw;                                     // Restore x and y signs
    outputTangent.z    *= ztztSigns.z;                                      // Restore z sign
    outputTangent.w     = ztztSigns.w;                                      // Binormal sign

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

void main()
{
	gl_Position = projection * modelview * vec4(vPosition, 1.0);
	vFragPosition = vPosition;

	//Unpack normals
#if param_fullTangent == 1
	vNormalOut = vNormal.xyz;
	vTangentOut = vTangent;
#else
	vNormalOut = DecompressNormal(vNormal).yxz;
	vTangentOut = DecompressTangent(vNormal).yxzw;
#endif

	vTexCoordOut = vTexCoord;
}
