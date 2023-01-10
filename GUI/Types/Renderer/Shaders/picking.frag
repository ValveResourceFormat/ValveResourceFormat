#version 330

#define param_F_DEBUG_PICKER 0

uniform uint sceneObjectId;

#if param_F_DEBUG_PICKER == 1
    out vec4 outputColor;
#else
    out uint outputColor;
#endif

void main()
{
#if param_F_DEBUG_PICKER == 1
    outputColor = vec4(fract(float(sceneObjectId) / 7.0), fract(float(sceneObjectId) / 11.0), fract(float(sceneObjectId) / 13.0), 1.0);
#else
    outputColor = sceneObjectId;
#endif
}
