#version 460

#ifndef VRF_INSTANCING_INCLUDE
#define VRF_INSTANCING_INCLUDE

uniform mat3x4 transform;
uniform uint vTint;
uniform bool bIsInstancing = false;

uint instanceID : SV_InstanceID;

layout(std140, binding = 10) readonly buffer g_transformBuffer
{
    mat3x4 transforms[];
};

public struct ObjectData_t
{
    public mat4 transform;
    public vec4 vTint;
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

public mat3x4 GetTransform(uint index)
{
    return transforms[index];
}

public mat4 UnpackMatrix4(mat3x4 m)
{
    return mat4(
        m[0][0], m[1][0], m[2][0], 0,
        m[0][1], m[1][1], m[2][1], 0,
        m[0][2], m[1][2], m[2][2], 0,
        m[0][3], m[1][3], m[2][3], 1
    );
}

public mat4 CalculateObjectToWorldMatrix()
{
    return UnpackMatrix4(bIsInstancing ? transforms[instanceID] : transform);
}

vec4 GetObjectTintSrgb()
{
    return UnpackColor32(vTint);
}

public ObjectData_t GetObjectData()
{
    ObjectData_t object;
    object.transform = CalculateObjectToWorldMatrix();
    object.vTint = GetObjectTintSrgb();
    return object;
}

#endif

