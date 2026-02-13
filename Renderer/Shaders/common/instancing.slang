#version 460

#ifndef VRF_INSTANCING_INCLUDE
#define VRF_INSTANCING_INCLUDE

uniform mat3x4 transform;
uniform uint vTint;
uniform bool bIsInstancing = false;

#define S_SCENE_CUBEMAP_TYPE 0 // 0 = None, 1 = Per-batch cube map, 2 = Per-scene cube map array
#if (S_SCENE_CUBEMAP_TYPE > 0)
    uniform uvec4 g_nEnvMapVisibility;
    flat out uvec4 nEnvMapVisibility;
#endif

struct ObjectDataStandard
{
    uint TintAlpha;
    uint TransformIndex;
    uint VisibleLPV;
    uint Identification;
    uvec4 EnvMapVisibility;
};

layout(std430, binding = 9) readonly buffer g_objectBuffer
{
    ObjectDataStandard objects[];
};

layout(std430, binding = 10) readonly buffer g_transformBuffer
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

ObjectData_t GetObjectData()
{
    ObjectDataStandard objectData;

    if (bIsInstancing)
    {
        objectData = objects[gl_BaseInstance];
    }

    ObjectData_t object;
    object.transform = UnpackMatrix4(bIsInstancing ? transforms[objectData.TransformIndex] : transform);
    object.vTint = UnpackColor32(bIsInstancing ? objectData.TintAlpha : vTint);

    #if (S_SCENE_CUBEMAP_TYPE > 0)
        nEnvMapVisibility = bIsInstancing ? objectData.EnvMapVisibility : g_nEnvMapVisibility;
    #endif

    return object;
}

mat4 CalculateObjectToWorldMatrix()
{
    return GetObjectData().transform;
}

vec4 GetObjectTintSrgb()
{
    return GetObjectData().vTint;
}

#endif
