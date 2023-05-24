#version 330

#define param_F_DEBUG_PICKER 0

#define param_renderMode_ObjectId 0
#define param_renderMode_MeshId 0

uniform uint sceneObjectId;
uniform uint meshId;

#if param_F_DEBUG_PICKER == 1
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
        #if param_renderMode_ObjectId == 1
            outputColor = ColorFromId(sceneObjectId, 0);
        #elif param_renderMode_MeshId == 1
            outputColor = ColorFromId(meshId, 19);
        #endif
    }
#else
    out uvec2 outputColor;
    void main()
    {
        outputColor = uvec2(sceneObjectId, meshId);
    }
#endif
