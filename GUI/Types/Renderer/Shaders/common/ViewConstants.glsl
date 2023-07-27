layout(std140) uniform ViewConstants {
    mat4 g_matViewToProjection;
    // TODO: not here
    mat4 vLightPosition;
    vec4 vLightColor;
    vec3 g_vCameraPositionWs;
    float g_flTime;
};
