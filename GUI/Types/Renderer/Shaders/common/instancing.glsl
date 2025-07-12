#version 460

uniform mat3x4 transform;
uniform uint vTint;
uniform bool bIsInstancing = false;

layout(std140, binding = 3) readonly buffer g_transformBuffer
{
    mat3x4 transforms[];
};

struct ObjectData_t
{
    mat4 transform;
    vec4 vTint;
};

vec4 UnpackColor32(uint nColor)
{
    vec4 vResult;
    vResult.a = (nColor >> 24) & 0xff;
    vResult.b = (nColor >> 16) & 0xff;
    vResult.g = (nColor >> 8) & 0xff;
    vResult.r = (nColor >> 0) & 0xff;
    vResult.rgba *= ( 1 / 255.f );
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

mat4 CalculateObjectToWorldMatrix()
{
    return UnpackMatrix4(bIsInstancing ? transforms[gl_InstanceID] : transform);
}

vec4 GetObjectTint()
{
    return UnpackColor32(vTint);
}

ObjectData_t GetObjectData()
{
    ObjectData_t object;
    object.transform = CalculateObjectToWorldMatrix();
    object.vTint = GetObjectTint();
    return object;
}
