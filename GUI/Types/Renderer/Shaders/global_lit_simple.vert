#version 460

//Includes - resolved by VRF
#include "compression.incl"
#include "animation.incl"
//End of includes

//Parameter defines - These are default values and can be overwritten based on material/model parameters
#define F_PAINT_VERTEX_COLORS 0
//End of parameter defines

layout (location = 0) in vec3 vPOSITION;
in vec4 vNORMAL;
#if (D_COMPRESSED_NORMALS_AND_TANGENTS == 0)
    in vec3 vTANGENT;
#endif
in vec2 vTEXCOORD;
#if (F_PAINT_VERTEX_COLORS == 1)
    in vec4 vTEXCOORD2;
    out vec4 vVertexColorOut;
#endif

out vec3 vFragPosition;
out vec3 vNormalOut;
out vec3 vTangentOut;
out vec3 vBitangentOut;
out vec2 vTexCoordOut;
out vec4 vTintColorFadeOut;

uniform vec4 g_vTexCoordOffset;
uniform vec4 g_vTexCoordScale;

uniform mat4 uProjectionViewMatrix;
uniform mat4 transform;
uniform float g_flTime;

uniform vec4 m_vTintColorSceneObject;
uniform vec3 m_vTintColorDrawCall;

uniform vec4 g_vColorTint;

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

    vTintColorFadeOut.rgb = m_vTintColorSceneObject.rgb * m_vTintColorDrawCall * g_vColorTint.rgb;
    vTintColorFadeOut.a = m_vTintColorSceneObject.a * g_vColorTint.a;

    vTexCoordOut = vTEXCOORD * g_vTexCoordScale.xy + g_vTexCoordOffset.xy;

    #if (F_PAINT_VERTEX_COLORS == 1)
        vVertexColorOut = vTEXCOORD2 / 255.0;
    #endif
}
