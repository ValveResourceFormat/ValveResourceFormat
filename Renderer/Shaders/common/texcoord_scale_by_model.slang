#version 460

#ifndef VRF_TEXCOORD_SCALE_BY_MODEL_INCLUDE
#define VRF_TEXCOORD_SCALE_BY_MODEL_INCLUDE

uniform int g_nScaleTexCoordUByModelScaleAxis = 0; // 0="None", 1="X", 2="Y", 3="Z"
uniform int g_nScaleTexCoordVByModelScaleAxis = 0;
uniform int g_nScaleTexCoordUByModelScaleOrigin = 0; // 0="Left", 1="Center", 2="Right"
uniform int g_nScaleTexCoordVByModelScaleOrigin = 2; // 0="Top", 1="Center", 2="Bottom"

uniform int g_nScaleTexCoord2UByModelScaleAxis = 0;
uniform int g_nScaleTexCoord2VByModelScaleAxis = 0;
uniform int g_nScaleTexCoord2UByModelScaleOrigin = 0;
uniform int g_nScaleTexCoord2VByModelScaleOrigin = 2;

float ScaleTexCoordByModel(float texCoord, int axis, float origin, vec3 modelScale)
{
    if (axis == 0 || texCoord < 0.0)
    {
        return texCoord;
    }

    return (texCoord - origin) * modelScale[axis - 1] + origin;
}

// UV1 in xy, UV2 in zw.
vec4 ApplyTexCoordScaleByModel(vec4 texCoords, mat3x4 transform)
{
    vec3 modelScale = vec3(
        length(vec3(transform[0].x, transform[1].x, transform[2].x)),
        length(vec3(transform[0].y, transform[1].y, transform[2].y)),
        length(vec3(transform[0].z, transform[1].z, transform[2].z)));

    // Only UV1 counts its V origin from the bottom edge.
    texCoords.x = ScaleTexCoordByModel(texCoords.x, g_nScaleTexCoordUByModelScaleAxis, float(g_nScaleTexCoordUByModelScaleOrigin) * 0.5, modelScale);
    texCoords.y = ScaleTexCoordByModel(texCoords.y, g_nScaleTexCoordVByModelScaleAxis, 1.0 - float(g_nScaleTexCoordVByModelScaleOrigin) * 0.5, modelScale);
    texCoords.z = ScaleTexCoordByModel(texCoords.z, g_nScaleTexCoord2UByModelScaleAxis, float(g_nScaleTexCoord2UByModelScaleOrigin) * 0.5, modelScale);
    texCoords.w = ScaleTexCoordByModel(texCoords.w, g_nScaleTexCoord2VByModelScaleAxis, float(g_nScaleTexCoord2VByModelScaleOrigin) * 0.5, modelScale);

    return texCoords;
}

#endif
