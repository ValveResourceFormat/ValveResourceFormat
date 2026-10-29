#version 460

#include "common/view_constants.slang"
#include "common/utils.slang"

// Builds the volumetric fog volume of a view: for every pixel column of the froxel grid it walks the slices
// from the camera outwards, lights the fog of each slice with the fog lights and accumulates the in-scattered
// light and the remaining transmittance. The volume is read as scatter.rgb + color * transmittance.a.
//
// The slices are not filtered with their neighbours and not blended with the previous frame's volume.
layout(local_size_x = 8, local_size_y = 8) in;

layout(binding = 0, rgba16f) uniform writeonly image3D g_tFogVolumeOut;

const int MAX_FOG_VOLUMES = 64;
const int MAX_FOG_LIGHTS = 64;

uniform vec3 g_vFogGridSize;

// Slices sit at near + range * s^2 along the view direction, with s the slice center over the slice count
uniform vec2 g_vFogNearRange;

// Scattering scale (xyz unused), fade-in scale and bias
uniform vec4 g_vFogScatteringFadeIn;

uniform int g_nFogVolumeCount;
uniform vec4 g_vFogVolumeBoundsBias[MAX_FOG_VOLUMES]; // xyz: world to 0..1 bias, w: 1 for a sphere falloff
uniform vec4 g_vFogVolumeBoundsScale[MAX_FOG_VOLUMES]; // xyz: world to 0..1 scale, w: strength
uniform vec4 g_vFogVolumeExponent[MAX_FOG_VOLUMES]; // x: falloff exponent

uniform int g_nFogLightCount;
uniform vec4 g_vFogLightPosition_Spot[MAX_FOG_LIGHTS]; // w: 1 for a spot light
uniform vec4 g_vFogLightDirection_OuterCos[MAX_FOG_LIGHTS];
uniform vec4 g_vFogLightColor_InnerCos[MAX_FOG_LIGHTS];
uniform vec4 g_vFogLightFalloff[MAX_FOG_LIGHTS]; // xy: distance terms, z: range squared, w: bias

// Density of the fog at a position: the strongest volume, never zero
float GetFogDensity(vec3 vPositionWs)
{
    float flDensity = 0.001;

    for (int i = 0; i < g_nFogVolumeCount; i++)
    {
        vec3 vUnit = vPositionWs * g_vFogVolumeBoundsScale[i].xyz + g_vFogVolumeBoundsBias[i].xyz;

        if (any(lessThan(vUnit, vec3(0.0))) || any(greaterThan(vUnit, vec3(1.0))))
        {
            continue;
        }

        vec3 vEdge = abs(vUnit * 2.0 - 1.0);
        float flEdge = g_vFogVolumeBoundsBias[i].w == 0.0 ? max(vEdge.x, max(vEdge.y, vEdge.z)) : length(vEdge);
        float flVolume = pow(max(1.0 - flEdge, 0.0), g_vFogVolumeExponent[i].x) * g_vFogVolumeBoundsScale[i].w;

        flDensity = max(flDensity, flVolume);
    }

    return saturate(flDensity);
}

// Light the fog scatters towards the eye at a position
vec3 GetFogLighting(vec3 vPositionWs)
{
    vec3 vLighting = vec3(0.0);

    for (int i = 0; i < g_nFogLightCount; i++)
    {
        vec3 vToLight = g_vFogLightPosition_Spot[i].xyz - vPositionWs;
        float flDistanceSquared = dot(vToLight, vToLight);

        if (flDistanceSquared > g_vFogLightFalloff[i].z)
        {
            continue;
        }

        float flCone = 1.0;

        if (g_vFogLightPosition_Spot[i].w != 0.0)
        {
            float flCosAngle = dot(normalize(vToLight), -g_vFogLightDirection_OuterCos[i].xyz);
            float flOuterCos = g_vFogLightDirection_OuterCos[i].w;
            float flInnerCos = g_vFogLightColor_InnerCos[i].w;

            if (flCosAngle <= flOuterCos)
            {
                continue;
            }

            flCone = saturate((flCosAngle - flOuterCos) / max(flInnerCos - flOuterCos, 0.01));
        }

        // TODO: cookies and shadows. A shadowed light gives no fog outside its shadow map or where it is fully
        // occluded, and is scaled by the shadow twice elsewhere, so walls keep it out of the rooms behind them
        float flClampedDistanceSquared = max(flDistanceSquared, 36.0);
        float flAttenuation = saturate(1.0 / dot(vec2(sqrt(flClampedDistanceSquared), flClampedDistanceSquared), g_vFogLightFalloff[i].xy) - g_vFogLightFalloff[i].w);

        vLighting += g_vFogLightColor_InnerCos[i].rgb * flAttenuation * flCone;
    }

    return vLighting;
}

void main()
{
    ivec3 vGridSize = ivec3(g_vFogGridSize);
    ivec2 vColumn = ivec2(gl_GlobalInvocationID.xy);

    if (any(greaterThanEqual(vColumn, vGridSize.xy)))
    {
        return;
    }

    // Ray through the column center, scaled to advance one unit along the view direction per unit of depth
    vec2 vNdc = (vec2(vColumn) + 0.5) / vec2(vGridSize.xy) * 2.0 - 1.0;
    // The projection inverse gives a point on the near plane in view space, which is also the ray from the camera
    vec4 vViewRay = g_matProjectionToWorld * vec4(vNdc, 1.0, 1.0);
    vec3 vRay = transpose(mat3(g_matWorldToView)) * (vViewRay.xyz / vViewRay.w);
    vRay /= dot(vRay, g_vCameraDirWs);

    float flScattering = g_vFogScatteringFadeIn.x * length(vRay);

    vec3 vScatter = vec3(0.0);
    float flOpticalDepth = 0.0;
    float flPreviousDepth = 0.0;

    for (int nSlice = 0; nSlice < vGridSize.z; nSlice++)
    {
        float flSlice = (float(nSlice) + 0.5) / float(vGridSize.z);
        float flDepth = g_vFogNearRange.y * flSlice * flSlice + g_vFogNearRange.x;
        vec3 vPositionWs = g_vCameraPositionWs + vRay * flDepth;

        float flDensity = GetFogDensity(vPositionWs);
        vec3 vLighting = GetFogLighting(vPositionWs);

        float flFadeIn = saturate(flDepth * g_vFogScatteringFadeIn.z + g_vFogScatteringFadeIn.w);
        float flExtinction = flDensity * (flDepth - flPreviousDepth) * flScattering * flFadeIn;

        vScatter += vLighting * flExtinction * saturate(exp(-flOpticalDepth));
        flOpticalDepth += flExtinction;
        flPreviousDepth = flDepth;

        imageStore(g_tFogVolumeOut, ivec3(vColumn, nSlice), vec4(vScatter, saturate(exp(-flOpticalDepth))));
    }
}
