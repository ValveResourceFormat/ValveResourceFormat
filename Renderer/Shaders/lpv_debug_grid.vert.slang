#version 460

// One point per light probe sample, in the volume's local space
#include "common/instancing_base.slang"

in vec3 vPOSITION;

out vec3 vSampleCenterWs;
flat out uint vObjectIndex;

void main()
{
    InstanceDataStandard instanceData = g_instanceBuffer[gl_BaseInstance];

    vObjectIndex = instanceData.Identification;
    vSampleCenterWs = vec4(vPOSITION, 1.0) * g_transformBuffer[instanceData.TransformIndex];
}
