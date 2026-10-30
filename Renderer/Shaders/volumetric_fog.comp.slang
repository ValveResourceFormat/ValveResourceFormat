#version 460

#include "common/view_constants.slang"
#include "common/utils.slang"
#include "common/lighting_constants.slang"

// The shadows and cookies of the lights are the ones the scene renders for its own shading
#include "common/shadow_pcf.slang"
#include "common/light_cookie.slang"

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
uniform vec4 g_vFogVolumeExponent[MAX_FOG_VOLUMES / 4]; // falloff exponents, four volumes per element, to stay within the uniform limit of some drivers

// The lights up to the static count are baked into the fog's light clipmaps, the rest are lit per slice
uniform int g_nFogLightCount;
uniform int g_nFogStaticLightCount;
uniform int g_nFogLightStorageIs8Bit;
uniform vec4 g_vFogLightPosition_Spot[MAX_FOG_LIGHTS]; // w: 1 for a spot light
uniform vec4 g_vFogLightDirection_OuterCos[MAX_FOG_LIGHTS];
uniform vec4 g_vFogLightColor_InnerCos[MAX_FOG_LIGHTS];
uniform vec4 g_vFogLightFalloff[MAX_FOG_LIGHTS]; // xy: distance terms, z: range squared, w: bias
uniform vec4 g_vFogLightParams[MAX_FOG_LIGHTS]; // x: index in the lighting constants or -1, y: 1 when the fog shadows the light

// With 8 bit storage, the density and the baked light only exist inside nested cubes around the camera.
// xyz: center of the cubes, w: half extent of the outermost one, zero or less for no limit
uniform vec4 g_vFogClipmapCenterExtent;

bool IsInsideFogClipmap(vec3 vPositionWs)
{
    float flExtent = g_vFogClipmapCenterExtent.w;

    if (flExtent <= 0.0)
    {
        return true;
    }

    vec3 vOffset = abs(vPositionWs - g_vFogClipmapCenterExtent.xyz);
    return max(vOffset.x, max(vOffset.y, vOffset.z)) <= flExtent;
}

// Density of the fog at a position: the strongest volume. With 8 bit storage it is stored in 8 bits and may be zero,
// otherwise it never is.
float GetFogDensity(vec3 vPositionWs)
{
    bool bIs8Bit = g_nFogLightStorageIs8Bit != 0;

    if (bIs8Bit && !IsInsideFogClipmap(vPositionWs))
    {
        return 0.0;
    }

    float flDensity = bIs8Bit ? 0.0 : 0.001;

    for (int i = 0; i < g_nFogVolumeCount; i++)
    {
        vec3 vUnit = vPositionWs * g_vFogVolumeBoundsScale[i].xyz + g_vFogVolumeBoundsBias[i].xyz;

        if (any(lessThan(vUnit, vec3(0.0))) || any(greaterThan(vUnit, vec3(1.0))))
        {
            continue;
        }

        vec3 vEdge = abs(vUnit * 2.0 - 1.0);
        float flEdge = g_vFogVolumeBoundsBias[i].w == 0.0 ? max(vEdge.x, max(vEdge.y, vEdge.z)) : length(vEdge);
        float flVolume = pow(max(1.0 - flEdge, 0.0), g_vFogVolumeExponent[i / 4][i % 4]) * g_vFogVolumeBoundsScale[i].w;

        flDensity = max(flDensity, flVolume);
    }

    flDensity = saturate(flDensity);

    if (bIs8Bit)
    {
        flDensity = floor(flDensity * 255.0 + 0.5) / 255.0;
    }

    return flDensity;
}

// Unless the fog keeps its intermediate lighting as floats, the light clipmaps and slice lighting are stored as
// sqrt(light * 0.5) in 8 bits per channel, which caps the light they hold at 2.0.
// Lights that are bright close to their source, such as a lamp seen in haze, saturate here.
const float MAX_STORED_FOG_LIGHT = 2.0;

// How much of a light reaches a position through its shadow map, zero outside the light's tile of the map
float GetFogShadow(int nShadowSlot, vec3 vPositionWs)
{
    vec4 vClipPos = g_matLightWorldToShadow[nShadowSlot] * vec4(vPositionWs, 1.0);

    if (vClipPos.w <= 0.0)
    {
        return 0.0;
    }

    vec3 vNdc = vClipPos.xyz / vClipPos.w;
    vec4 vOffsetScale = g_vLightShadowOffsetScale[nShadowSlot];
    vec2 vTexelSize = 1.0 / vec2(textureSize(g_tBarnLightShadowDepth, 0));

    // The tile is taken in by three texels, so the filter stays inside it
    vec2 vTileEdge = vec2(1.0) - 3.0 * vTexelSize / vOffsetScale.zw;

    if (any(greaterThan(abs(vNdc.xy), vTileEdge)) || vNdc.z < 0.0 || vNdc.z > 1.0)
    {
        return 0.0;
    }

    float flShadowRef = saturate(vNdc.z + SHADOW_RECEIVER_DEPTH_BIAS);

    return ComputeBarnLightShadow_PCF_3x3_Gaussian(vNdc.xy * vOffsetScale.zw + vOffsetScale.xy, flShadowRef, vTexelSize);
}

// Light of one fog light at a position
vec3 GetFogLight(int i, vec3 vPositionWs)
{
    vec3 vColor = g_vFogLightColor_InnerCos[i].rgb;

    vec3 vLightPositionWs = g_vFogLightPosition_Spot[i].xyz;
    vec3 vToLight = vLightPositionWs - vPositionWs;
    float flDistanceSquared = dot(vToLight, vToLight);

    if (flDistanceSquared > g_vFogLightFalloff[i].z)
    {
        return vec3(0.0);
    }

    float flClampedDistanceSquared = max(flDistanceSquared, 36.0);
    float flAttenuation = saturate(1.0 / dot(vec2(sqrt(flClampedDistanceSquared), flClampedDistanceSquared), g_vFogLightFalloff[i].xy) - g_vFogLightFalloff[i].w);

    if (flAttenuation <= 0.0)
    {
        return vec3(0.0);
    }

    int nStoreIndex = int(g_vFogLightParams[i].x);
    vec3 vCone = vec3(1.0);

    if (g_vFogLightPosition_Spot[i].w != 0.0)
    {
        float flCosAngle = dot(normalize(vToLight), -g_vFogLightDirection_OuterCos[i].xyz);
        float flOuterCos = g_vFogLightDirection_OuterCos[i].w;
        float flInnerCos = g_vFogLightColor_InnerCos[i].w;

        if (flCosAngle <= flOuterCos)
        {
            return vec3(0.0);
        }

        vCone = vec3(saturate((flCosAngle - flOuterCos) / max(flInnerCos - flOuterCos, 0.01)));

        // The cookie takes the place of the cone falloff
        if (nStoreIndex >= 0 && g_vLightShadowParams[nStoreIndex].y >= 0.0)
        {
            vCone = CalculateDynamicLightCookie(uint(nStoreIndex), vLightPositionWs, vPositionWs, g_vLightShadowParams[nStoreIndex].y);
        }

        if (vCone == vec3(0.0))
        {
            return vec3(0.0);
        }
    }

    // The shadow scales the light twice, so a partly shadowed light is darker than it is in the scene
    float flShadow = 1.0;

    if (nStoreIndex >= 0 && g_vFogLightParams[i].y != 0.0 && g_vLightShadowParams[nStoreIndex].x >= 0.0)
    {
        flShadow = GetFogShadow(int(g_vLightShadowParams[nStoreIndex].x), vPositionWs);

        if (flShadow <= 0.0)
        {
            return vec3(0.0);
        }
    }

    return vColor * flShadow * flAttenuation * vCone * flShadow;
}

// Light the fog scatters towards the eye at a position
vec3 GetFogLighting(vec3 vPositionWs)
{
    vec3 vStaticLighting = vec3(0.0);
    vec3 vDynamicLighting = vec3(0.0);

    // The baked lights only exist inside the clipmaps
    bool bIs8Bit = g_nFogLightStorageIs8Bit != 0;
    bool bInsideClipmap = !bIs8Bit || IsInsideFogClipmap(vPositionWs);
    int nFirstLight = bInsideClipmap ? 0 : g_nFogStaticLightCount;

    for (int i = nFirstLight; i < g_nFogLightCount; i++)
    {
        vec3 vLight = GetFogLight(i, vPositionWs);

        if (i < g_nFogStaticLightCount)
        {
            vStaticLighting += vLight;
        }
        else
        {
            vDynamicLighting += vLight;
        }
    }

    if (!bIs8Bit)
    {
        return vStaticLighting + vDynamicLighting;
    }

    if (bInsideClipmap)
    {
        vec3 vStored = sqrt(min(vStaticLighting, vec3(MAX_STORED_FOG_LIGHT)) / MAX_STORED_FOG_LIGHT);
        vStored = floor(vStored * 255.0 + 0.5) / 255.0;
        vStaticLighting = vStored * vStored * MAX_STORED_FOG_LIGHT;
    }

    return min(vStaticLighting + vDynamicLighting, vec3(MAX_STORED_FOG_LIGHT));
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
        vec3 vLighting = vec3(0.0);

        if (flDensity > 0.0)
        {
            vLighting = GetFogLighting(vPositionWs);
        }

        float flFadeIn = saturate(flDepth * g_vFogScatteringFadeIn.z + g_vFogScatteringFadeIn.w);
        float flExtinction = flDensity * (flDepth - flPreviousDepth) * flScattering * flFadeIn;

        vScatter += vLighting * flExtinction * saturate(exp(-flOpticalDepth));
        flOpticalDepth += flExtinction;
        flPreviousDepth = flDepth;

        imageStore(g_tFogVolumeOut, ivec3(vColumn, nSlice), vec4(vScatter, saturate(exp(-flOpticalDepth))));
    }
}
