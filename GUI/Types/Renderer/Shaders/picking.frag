#version 330

#define F_DEBUG_PICKER 0

#define renderMode_ObjectId 0
#define renderMode_MeshId 0

uniform uint sceneObjectId;
uniform uint meshId;

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
        #endif
    }
#else
    out uvec4 outputColor;
    void main()
    {
        outputColor = uvec4(sceneObjectId, meshId, 0, 0);
    }
#endif
