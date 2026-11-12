#version 460

// Vertex shader body of vr_complex. The entry file includes its game's features file first.

uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)

#include "../common/surface_base_vs.slang"
#include "../common/surface_model_tint_vs.slang"
#include "../features/detail_texture_vs.slang"

#if (F_FOLIAGE_ANIMATION > 0)
    #include "../features/foliage_vr_vs.slang"

    out vec4 vFoliageParamsOut;
#elif (F_VERTEX_COLOR == 1) || (F_PAINT_VERTEX_COLORS == 1)
    in vec4 vCOLOR;
#endif

#if (F_PRE_BAKED_VERTEX_ANIMATION == 1)
    #if (F_SECONDARY_UV == 0)
        in vec4 vTEXCOORD1;
    #endif

    uniform sampler2D g_tPositionKeyFrames;
    uniform sampler2D g_tRotationKeyFrames;
    uniform vec4 g_vPositionKeyFramesInvDim;
    uniform float g_flFrame;

    vec3 GetPreBakedVertexAnimationPosition(vec3 position)
    {
        float column = vTEXCOORD1.x;
        float invHeight = g_vPositionKeyFramesInvDim.y;

        vec3 pivot = textureLod(g_tPositionKeyFrames, vec2(column, 0.5 * invHeight), 0.0).zxy;
        vec3 offset = g_flFrame < 1.0
            ? textureLod(g_tPositionKeyFrames, vec2(column, 1.5 * invHeight), 0.0).zxy * g_flFrame
            : textureLod(g_tPositionKeyFrames, vec2(column, (g_flFrame + 0.5) * invHeight), 0.0).zxy;

        vec4 rotationFrom = textureLod(g_tRotationKeyFrames, vec2(column, (floor(g_flFrame) + 0.5) * invHeight), 0.0).zxyw;
        vec4 rotationTo = textureLod(g_tRotationKeyFrames, vec2(column, (ceil(g_flFrame) + 0.5) * invHeight), 0.0).zxyw;
        rotationTo = dot(rotationFrom, rotationTo) > 0.0 ? rotationTo : -rotationTo;
        vec4 rotation = normalize(mix(rotationFrom, rotationTo, fract(g_flFrame)));

        return QuaternionRotation(position - pivot, rotation) + pivot + offset;
    }
#endif

void main()
{
    WriteSurfaceVertex(false);

#if (F_PRE_BAKED_VERTEX_ANIMATION == 1)
    vFragPosition = vec4(GetPreBakedVertexAnimationPosition(vPOSITION), 1.0) * CalculateSkinTransform();
    gl_Position = g_matWorldToProjection * vec4(vFragPosition, 1.0);
#endif

#if (F_FOLIAGE_ANIMATION == 4)
    vFoliageParamsOut = vec4(0.0, 0.0, 0.0, 1.0);
#elif (F_FOLIAGE_ANIMATION > 0)
    // Interpolate out for debug visualization
    vFoliageParamsOut = vec4(vTEXCOORD3.xyz, 1.0);
#endif

#if (F_FOLIAGE_ANIMATION > 0) && (F_FOLIAGE_ANIMATION != 2)
    vFragPosition = AnimateVrFoliage();
    vCentroidNormalOut = vNormalOut;
    gl_Position = g_matWorldToProjection * vec4(vFragPosition, 1.0);
#endif

#if (F_FOLIAGE_ANIMATION == 0) && ((F_VERTEX_COLOR == 1) || (F_PAINT_VERTEX_COLORS == 1))
    vVertexColorOut *= SrgbGammaToLinear(vCOLOR);
#endif

    WriteDetailTexCoords();
}
