#version 460

#include "unlitgeneric_features.slang"
#include "common_vs.slang"
#include "../common/morph.slang"

in vec3 vNORMAL;
in vec4 vTANGENT;

out vec3 vFragPosition;
out vec3 vNormalOut;
out vec4 vTangentOut;
out vec2 vTexCoordOut;
out vec4 vVertexColorOut;
out float vWrinkleOut;

#if (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
    in vec4 vCOLOR1;
    out vec4 vBakedLightOut;
#endif

// The wrinkle weight of the normal and wrinkle field of the morph composite
float GetMorphWrinkle()
{
    #if (F_MORPH_SUPPORTED == 1)
        if (IS_MORPHED)
        {
            return texelFetch(g_tCompositeMorphTextureAtlas, GetMorphTexel(MORPH_FIELD_NORMAL_WRINKLE), 0).w;
        }
    #endif

    return 0.0;
}

void main()
{
    mat3x4 objectToWorld = CalculateSkinTransform();

    vFragPosition = vec4(vPOSITION + GetMorphOffset(), 1.0) * objectToWorld;
    vNormalOut = TransformDirectionToWorld(objectToWorld, ApplyMorphNormal(vNORMAL));
    vTangentOut = TransformTangentToWorld(objectToWorld, vTANGENT);
    gl_Position = g_matWorldToProjection * vec4(vFragPosition, 1.0);

    vTexCoordOut = GetSource1BaseTexCoord(vTEXCOORD);

    vVertexColorOut = GetSource1VertexColor();
    vWrinkleOut = GetMorphWrinkle();

#if (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
    vBakedLightOut = vCOLOR1;
#endif
}
