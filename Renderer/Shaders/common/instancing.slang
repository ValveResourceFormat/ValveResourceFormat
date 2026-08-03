#version 460

#ifndef VRF_INSTANCING_INCLUDE
#define VRF_INSTANCING_INCLUDE

uniform mat3x4 transform;
uniform uint vTint;
uniform bool bIsInstancing = false;

#if (S_SCENE_CUBEMAP_TYPE == 1)
    flat out uint g_nEnvMapIndex;
#elif (S_SCENE_CUBEMAP_TYPE == 2)
    flat out uvec4 g_nEnvMapVisibility;
#endif

#if (D_BAKED_LIGHTING_FROM_PROBE > 0)
    flat out uint g_nVisibleLPV;
#endif

struct ObjectDataStandard
{
    uint TintAlpha;
    uint TransformIndex;
    uint VisibleLPV;
    uint Identification;
    uvec4 EnvMapVisibility;
};

layout(std430, binding = 0) readonly buffer g_objectBuffer
{
    ObjectDataStandard objects[];
};

layout(std430, binding = 1) readonly buffer g_transformBuffer
{
    mat3x4 transforms[];
};

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

void SetObjectInterpolators(ObjectDataStandard objectData)
{
    #if (S_SCENE_CUBEMAP_TYPE == 1)
        g_nEnvMapIndex = objectData.VisibleLPV >> 16u;
    #elif (S_SCENE_CUBEMAP_TYPE == 2)
        g_nEnvMapVisibility = objectData.EnvMapVisibility;
    #endif

    #if (D_BAKED_LIGHTING_FROM_PROBE > 0)
        g_nVisibleLPV = objectData.VisibleLPV & 0xFFFFu;
    #endif
}

ObjectData_t GetObjectData()
{
    ObjectDataStandard objectData = objects[gl_BaseInstance];

    SetObjectInterpolators(objectData);

    ObjectData_t object;
    object.vTint = UnpackColor32(bIsInstancing ? objectData.TintAlpha : vTint);

    return object;
}

mat3x4 CalculateObjectToWorldMatrix()
{
    ObjectDataStandard objectData = objects[gl_BaseInstance];

    SetObjectInterpolators(objectData);

    return bIsInstancing ? transforms[objectData.TransformIndex + gl_InstanceID] : transform;
}

vec4 GetObjectTintSrgb()
{
    return GetObjectData().vTint;
}

#endif
