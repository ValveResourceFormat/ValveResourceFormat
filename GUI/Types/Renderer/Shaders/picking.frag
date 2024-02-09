#version 460

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
        #if renderMode_ObjectId == 1
            outputColor = ColorFromId(sceneObjectId, 0u);
        #elif renderMode_MeshId == 1
            outputColor = ColorFromId(meshId, 19u);
        #elif renderMode_ShaderId == 1
            float idLowered = float(shaderId) / 7000.0;
            outputColor = vec4(fract(idLowered / 7.0), fract(idLowered / 11.0), fract(idLowered / 13.0), 1.0);
        #elif renderMode_ShaderProgramId == 1
            outputColor = ColorFromId(shaderProgramId, 29u);
        #endif
    }
#else
    out uvec4 outputColor;
    void main()
    {
        outputColor = uvec4(sceneObjectId, meshId, isSkybox, 0);
    }
#endif
