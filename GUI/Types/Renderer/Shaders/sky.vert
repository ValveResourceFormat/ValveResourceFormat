#version 330

layout (location = 0) in vec3 aVertexPosition;
out vec3 vSkyLookupInterpolant;

uniform mat4 g_matWorldToProjection;
uniform mat4 g_matWorldToView;
uniform mat4 g_matViewToProjection;

uniform vec3 g_vCameraPositionWs;

void main()
{
    vec4 vPositionWs = (g_matWorldToProjection * mat4(mat3(g_matWorldToView))) * vec4(aVertexPosition, 1.0);
    gl_Position = vPositionWs.xyww;
    vSkyLookupInterpolant = aVertexPosition;
}
