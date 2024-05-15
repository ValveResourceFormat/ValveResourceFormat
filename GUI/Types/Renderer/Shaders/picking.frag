#version 460

#include "common/ViewConstants.glsl"

#define F_DEBUG_PICKER 0

#define renderMode_ObjectId 0
#define renderMode_MeshId 0
#define renderMode_ShaderId 0
#define renderMode_ShaderProgramId 0

uniform uint sceneObjectId;
uniform uint meshId;
uniform uint shaderId;
uniform uint shaderProgramId;
uniform uint isSkybox;

#if F_DEBUG_PICKER == 1
    out vec4 outputColor;

    vec4 ColorFromId(uint id, uint offset)
    {
        return vec4(
            fract(float(id + offset) / 7.0),
            fract(float(id + offset) / 11.0),
            fract(float(id + offset) / 13.0),
            1.0
        );
    }

    void main()
    {
        if (g_iRenderMode == renderMode_ObjectId)
        {
            outputColor = ColorFromId(sceneObjectId, 0u);
        }
        else if (g_iRenderMode == renderMode_MeshId)
        {
            outputColor = ColorFromId(meshId, 19u);
        }
        else if (g_iRenderMode == renderMode_ShaderId)
        {
            float idLowered = float(shaderId) / 7000.0;
            outputColor = vec4(fract(idLowered / 7.0), fract(idLowered / 11.0), fract(idLowered / 13.0), 1.0);
        }
        else if (g_iRenderMode == renderMode_ShaderProgramId)
        {
            outputColor = ColorFromId(shaderProgramId, 29u);
        }
    }
#else
    out uvec4 outputColor;
    void main()
    {
        outputColor = uvec4(sceneObjectId, meshId, isSkybox, 0);
    }
#endif
