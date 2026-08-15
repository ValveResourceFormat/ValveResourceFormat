#version 460

#ifndef VRF_MORPH_INCLUDE
#define VRF_MORPH_INCLUDE

#define F_MORPH_SUPPORTED 0 // most shaders
#define bMorphed (morphVertexIdOffset != -1)

#if (F_MORPH_SUPPORTED == 1)
    uniform sampler2D morphCompositeTexture; // reserved
    uniform int morphVertexIdOffset;

    #define MORPH_FIELD_POSITION_SPEED 0
    #define MORPH_FIELD_NORMAL_WRINKLE 1

    // The composite holds two fields of the same size side by side: position/speed on the left,
    // normal/wrinkle on the right. A field is therefore half of the composite wide, and that is
    // also the row stride the morph set was laid out with.
    vec2 getMorphUV(int field)
    {
        vec2 compositeSize = vec2(textureSize(morphCompositeTexture, 0));
        float fieldWidth = compositeSize.x * 0.5;

        float vertexId = float(gl_VertexID + morphVertexIdOffset);
        float column = mod(vertexId, fieldWidth);
        float row = floor(vertexId / fieldWidth);

        return vec2(
            (0.5 + column) / compositeSize.x + 0.5 * float(field),
            1.0 - ((0.5 + row) / compositeSize.y)
        );
    }
#endif

vec3 getMorphOffset()
{
    #if (F_MORPH_SUPPORTED == 1)
        if (bMorphed)
        {
            return textureLod(morphCompositeTexture, getMorphUV(MORPH_FIELD_POSITION_SPEED), 0).xyz;
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
            return normalize(normal + textureLod(morphCompositeTexture, getMorphUV(MORPH_FIELD_NORMAL_WRINKLE), 0).xyz);
        }
    #endif

    return normal;
}

#endif
