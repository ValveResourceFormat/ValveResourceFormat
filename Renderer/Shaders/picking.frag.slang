#version 460

#include "common/ViewConstants.slang"

#define F_DEBUG_PICKER 0 // VRF: visualize picking IDs with colors

#define renderMode_ObjectId 0
#define renderMode_MeshId 0
#define renderMode_ShaderId 0
#define renderMode_ShaderProgramId 0
#define renderMode_Meshlets 0

flat in uint sceneObjectId;
flat in uint meshletId;

uniform uint meshId;
uniform uint shaderId;
uniform uint shaderProgramId;
uniform uint isSkybox;

#if F_DEBUG_PICKER == 1
    out vec4 outputColor;

    vec4 ColorFromId(uint id, uint offset)
    {
        uint hash = id + offset;
        hash = hash * 1664525u + 1013904223u; // LCG constants
        
        uint r = hash;
        hash = hash * 1664525u + 1013904223u;
        uint g = hash;
        hash = hash * 1664525u + 1013904223u;
        uint b = hash;
        
        return vec4(
            float(r & 0xFFu) / 255.0,
            float(g & 0xFFu) / 255.0,
            float(b & 0xFFu) / 255.0,
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
        else if (g_iRenderMode == renderMode_Meshlets)
        {
            uint combined = meshletId | (sceneObjectId << 8);
            outputColor = meshletId == 0
                ? vec4(0.9)
                : mix(ColorFromId(combined, 39u), vec4(1.0), 0.1);
        }
    }
#else
    out uvec4 outputColor;
    void main()
    {
        outputColor = uvec4(sceneObjectId, meshId, isSkybox, 0);
    }
#endif
