#version 460

#define D_POINT_TO_CUBE 0

layout(points) in;

#if (D_POINT_TO_CUBE == 1)
    layout(triangle_strip, max_vertices = 12) out;
#else
    layout(triangle_strip, max_vertices = 4) out;
#endif

in vec3 vSampleCenterWs[];
flat in uint vObjectIndex[];

out vec3 vFragPosition;
out vec3 vCubeNormal;
flat out vec3 vSampleCenter;
flat out uint g_nObjectIndex;

// Never written, the grid is lit from its probe; declared so the shared lighting links in every combo
#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    centroid out vec3 vLightmapUVScaled;
#elif (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
    out vec3 vPerVertexLightingOut;
#endif

uniform float g_flPointSize = 4.0;

#include "common/ViewConstants.slang"

void EmitCorner(vec3 position, vec3 normal, vec3 center)
{
    vFragPosition = position;
    vCubeNormal = normal;
    vSampleCenter = center;
    g_nObjectIndex = vObjectIndex[0];
    gl_Position = g_matWorldToProjection * vec4(position, 1.0);
    EmitVertex();
}

void main()
{
    vec3 center = vSampleCenterWs[0];
    vec3 toCamera = g_vCameraPositionWs - center;
    float distanceToCamera = length(toCamera);
    float radius = g_flPointSize;

#if (D_POINT_TO_CUBE == 1)
    // Only the three faces turned towards the camera are visible
    for (int axis = 0; axis < 3; axis++)
    {
        vec3 normal = vec3(0.0);
        normal[axis] = toCamera[axis] >= 0.0 ? 1.0 : -1.0;

        vec3 u = vec3(0.0);
        vec3 v = vec3(0.0);
        u[(axis + 1) % 3] = radius;
        v[(axis + 2) % 3] = radius;

        vec3 faceCenter = center + normal * radius;

        EmitCorner(faceCenter - u - v, normal, center);
        EmitCorner(faceCenter + u - v, normal, center);
        EmitCorner(faceCenter - u + v, normal, center);
        EmitCorner(faceCenter + u + v, normal, center);
        EndPrimitive();
    }
#else
    // Inside the sphere there is nothing to see of it
    if (distanceToCamera <= radius)
    {
        return;
    }

    vec3 forward = toCamera / distanceToCamera;
    vec3 worldUp = abs(forward.z) < 0.999 ? vec3(0.0, 0.0, 1.0) : vec3(1.0, 0.0, 0.0);
    vec3 right = normalize(cross(worldUp, forward));
    vec3 up = cross(forward, right);

    // The sphere's outline under perspective is wider than its radius, measured in the plane through its center
    float extent = radius * distanceToCamera / sqrt(distanceToCamera * distanceToCamera - radius * radius);
    right *= extent;
    up *= extent;

    EmitCorner(center - right - up, forward, center);
    EmitCorner(center + right - up, forward, center);
    EmitCorner(center - right + up, forward, center);
    EmitCorner(center + right + up, forward, center);
    EndPrimitive();
#endif
}
