layout(std140) uniform ViewConstants {
    mat4 g_matViewToProjection;
    mat4 g_matWorldToProjection;
    mat4 g_matWorldToView;
    vec3 g_vCameraPositionWs;
    float g_flTime;
};
