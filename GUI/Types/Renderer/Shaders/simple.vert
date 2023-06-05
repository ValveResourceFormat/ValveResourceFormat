#version 330

//Includes - resolved by VRF
#include "compression.incl"
#include "animation.incl"
//End of includes

//Parameter defines - These are default values and can be overwritten based on material/model parameters
#define fulltangent 1
#define D_BAKED_LIGHTING_FROM_LIGHTMAP 0
#define D_BAKED_LIGHTING_FROM_VERTEX_STREAM 0
#define D_BAKED_LIGHTING_FROM_LIGHTPROBE 0

#define F_VERTEX_COLOR 0
#define F_LAYERS 0
#define simple_2way_blend 0
//End of parameter defines

layout (location = 0) in vec3 vPOSITION;
in vec4 vNORMAL;
in vec2 vTEXCOORD;
#if D_BAKED_LIGHTING_FROM_LIGHTMAP == 1
    in vec2 vLightmapUV;
    out vec3 vLightmapUVScaled;
    uniform vec4 g_vLightmapUvScale;
#endif
#if F_LAYERS > 0
    #if simple_2way_blend == 1
        #define vBLEND_COLOR vTEXCOORD2
    #else
        // ligthtmappedgeneric - real semantic index is 4
        #define vBLEND_COLOR vTEXCOORD3
    #endif
    in vec4 vBLEND_COLOR;
    out vec4 vColorBlendValues;
#endif
#if fulltangent == 1
    in vec3 vTANGENT;
#endif
#if F_VERTEX_COLOR == 1
    in vec4 vCOLOR;
    out vec4 vColorOut;
#endif

out vec3 vFragPosition;

out vec3 vNormalOut;
out vec3 vTangentOut;
out vec3 vBitangentOut;

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
#if fulltangent == 1
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

#if D_BAKED_LIGHTING_FROM_LIGHTMAP == 1
    vLightmapUVScaled = vec3(vLightmapUV * g_vLightmapUvScale.xy, 0);
#endif

#if F_VERTEX_COLOR == 1
    vColorOut = vCOLOR;
#endif

#if F_LAYERS > 0
    vColorBlendValues = vBLEND_COLOR / 255.0f;
#endif

}
