#version 460

#ifndef VRF_INSTANCING_INCLUDE
#define VRF_INSTANCING_INCLUDE

#include "instancing_base.slang"

flat out uint g_nObjectIndex;

struct ObjectData_t
{
    vec4 vTint;
};

vec4 UnpackColor32(uint nColor)
{
    vec4 vResult;
    vResult.a = (nColor >> 24) & 0xff;
    vResult.b = (nColor >> 16) & 0xff;
    vResult.g = (nColor >> 8) & 0xff;
    vResult.r = (nColor >> 0) & 0xff;
    vResult.rgba *= ( 1.0 / 255.0 );
    return vResult;
}

mat4 UnpackMatrix4(mat3x4 m)
{
    return mat4(
        m[0][0], m[1][0], m[2][0], 0,
        m[0][1], m[1][1], m[2][1], 0,
        m[0][2], m[1][2], m[2][2], 0,
        m[0][3], m[1][3], m[2][3], 1
    );
}

// Applies first, then second.
mat3x4 ConcatTransforms(mat3x4 first, mat3x4 second)
{
    mat3x4 result;

    for (int i = 0; i < 3; i++)
    {
        result[i] = (second[i].x * first[0]) + (second[i].y * first[1]) + (second[i].z * first[2]);
        result[i].w += second[i].w;
    }

    return result;
}

ObjectData_t GetObjectData()
{
    InstanceDataStandard instanceData = g_instanceBuffer[gl_BaseInstance];

    g_nObjectIndex = instanceData.Identification;

    ObjectData_t object;
    object.vTint = UnpackColor32(instanceData.TintAlpha);

    return object;
}

uint GetObjectBoneOffset()
{
    return g_instanceBuffer[gl_BaseInstance].TransformIndex + 2u;
}

mat3x4 CalculateObjectToWorldMatrix()
{
    InstanceDataStandard instanceData = g_instanceBuffer[gl_BaseInstance];

    g_nObjectIndex = instanceData.Identification;

    return g_transformBuffer[instanceData.TransformIndex + gl_InstanceID];
}

vec4 GetObjectTintSrgb()
{
    return GetObjectData().vTint;
}

#endif
