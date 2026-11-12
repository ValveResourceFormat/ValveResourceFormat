#version 460

#ifndef VRF_SKINNED_VERTEX_INCLUDE
#define VRF_SKINNED_VERTEX_INCLUDE

// Include after animation.slang, compression.slang and view_constants.slang, and after the shader declares
// vFragPosition, vNormalOut and vTangentOut.

#include "morph.slang"

// Skins the morphed vertex position and writes the clip position, the world position and the world tangent frame.
void WriteSkinnedPositionAndTangentFrame()
{
    mat3x4 skinTransform = CalculateSkinTransform();
    vec3 fragPosition = vec4(vPOSITION + GetMorphOffset(), 1.0) * skinTransform;
    gl_Position = g_matWorldToProjection * vec4(fragPosition, 1.0);
    vFragPosition = fragPosition;

    vec3 normal;
    vec4 tangent;
    GetOptionallyCompressedNormalTangent(normal, tangent);
    ComputeWorldTangentFrame(skinTransform, ApplyMorphNormal(normal), tangent, vNormalOut, vTangentOut);
}

#endif
