#version 460

#ifndef VRF_MORPH_INCLUDE
#define VRF_MORPH_INCLUDE

#include "instancing_base.slang"

#define F_MORPH_SUPPORTED 0 // most shaders
#define bMorphed (g_instanceBuffer[gl_BaseInstance].MorphVertexIdOffset != -1)

#if (F_MORPH_SUPPORTED == 1)
    uniform sampler2D g_tCompositeMorphTextureAtlas; // reserved

    #define MORPH_FIELD_POSITION_SPEED 0
    #define MORPH_FIELD_NORMAL_WRINKLE 1

    // The atlas holds two fields of the same size side by side: position/speed on the left, normal/wrinkle on the
    // right. A mesh's composite is a rect at the same place in both, laid out row by row at the morph set's width.
    ivec2 getMorphTexel(int field)
    {
        InstanceDataStandard instance = g_instanceBuffer[gl_BaseInstance];

        int vertexId = gl_VertexID + instance.MorphVertexIdOffset;
        int stride = int(instance.MorphAtlasStride);
        ivec2 origin = ivec2(instance.MorphAtlasOrigin & 0xFFFFu, instance.MorphAtlasOrigin >> 16u);

        ivec2 texel = origin + ivec2(vertexId % stride, vertexId / stride);
        texel.x += field * (textureSize(g_tCompositeMorphTextureAtlas, 0).x / 2);

        return texel;
    }
#endif

vec3 getMorphOffset()
{
    #if (F_MORPH_SUPPORTED == 1)
        if (bMorphed)
        {
            return texelFetch(g_tCompositeMorphTextureAtlas, getMorphTexel(MORPH_FIELD_POSITION_SPEED), 0).xyz;
        }
    #endif

    return vec3(0, 0, 0);
}

// The normal/wrinkle field carries the normal delta in rgb and the wrinkle weight in a.
// Nothing consumes wrinkle yet, it is only composited.
vec3 applyMorphNormal(vec3 normal)
{
    #if (F_MORPH_SUPPORTED == 1)
        if (bMorphed)
        {
            return normalize(normal + texelFetch(g_tCompositeMorphTextureAtlas, getMorphTexel(MORPH_FIELD_NORMAL_WRINKLE), 0).xyz);
        }
    #endif

    return normal;
}

#endif
