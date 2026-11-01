#version 460

#include "common/view_constants.slang"
#include "common/utils.slang"
#include "common/lighting_constants.slang"

// The shadows and cookies of the lights are the ones the scene renders for its own shading
#include "common/shadow_pcf.slang"
#include "common/light_cookie.slang"

// Barn, rect and omni2 lights are shaded as the scene shades them, but there is no surface to light
// and no pixel whose culling bins would choose the lights
#define VRF_VERTEX_STAGE
#define BARN_LIGHT_AREA_LIGHTS 2
#include "common/material_properties.slang"
#include "common/lighting_common.slang"
void CalculateShading(inout LightingTerms_t lighting, vec3 lightVector, vec3 lightColor, MaterialProperties_t mat)
{
}
#include "common/lighting_barn.slang"

// Builds the volumetric fog volume of a view: for every pixel column of the froxel grid it walks the slices
// from the camera outwards, lights the fog of each slice with the fog lights and accumulates the in-scattered
// light and the remaining transmittance. The volume is read as scatter.rgb + color * transmittance.a.
//
// The slices are not filtered with their neighbours and not blended with the previous frame's volume.
layout(local_size_x = 8, local_size_y = 8) in;

layout(binding = 0, rgba16f) uniform writeonly image3D g_tFogVolumeOut;

// Baked light of the fog, over the box of the fog controller
layout(binding = 30) uniform sampler3D g_tFogIrradiance;

uniform vec3 g_vFogGridSize;

// Slices sit at near + range * f(s) along the view direction, with s the slice center over the slice count
uniform vec2 g_vFogNearRange;

// The warp of the slices and one over its log2. Without a warp f is s^2, otherwise ((warp + 1)^s - 1) / warp
uniform vec2 g_vFogDepthWarp;

// Scattering scale (xyz unused), fade-in scale and bias
uniform vec4 g_vFogScatteringFadeIn;

// The tint of the fog in rgb, and the strength of the baked light, which is zero without it
uniform vec4 g_vFogTint_IndirectStrength;

// Maps a world position to the box of the baked light
uniform vec3 g_vFogIrradianceScale;
uniform vec3 g_vFogIrradianceBias;

struct FogVolume_t
{
    vec4 vUnitX; // world to 0..1 in the box of the volume, a row per axis
    vec4 vUnitY;
    vec4 vUnitZ;
    vec4 vParams; // shape, strength, falloff exponent, strength of the baked light or -1
    vec4 vHeightParams; // height scale, edge scale, 1 for a tint
    vec4 vTint;
};

layout(std430, binding = 10) readonly buffer FogVolumes
{
    FogVolume_t g_FogVolumes[];
};

uniform int g_nFogVolumeCount;

// The density of fog that no volume covers
uniform float g_flFogDensityFloor;

// The lights up to the static count are baked into the fog's light clipmaps, the rest are lit per slice
uniform int g_nFogLightCount;
uniform int g_nFogStaticLightCount;
uniform int g_nFogLightStorageIs8Bit;

struct FogLight_t
{
    vec4 vPosition_Spot; // w: 1 for a spot light
    vec4 vDirection_OuterCos;
    vec4 vColor_InnerCos;
    vec4 vFalloff; // xy: distance terms, z: range squared, w: bias
    vec4 vParams; // x: index in the lighting constants or -1, y: 1 when the fog shadows the light, z: first shadow face or -1
};

layout(std430, binding = 11) readonly buffer FogLights
{
    FogLight_t g_FogLights[];
};

// Shadow maps of the fog lights, tiles of one atlas. A spot light has one face, a point light six: +X, -X, +Y, -Y, +Z, -Z
layout(binding = 29) uniform sampler2DShadow g_tFogShadowDepth;

struct FogShadowFace_t
{
    mat4 matWorldToShadow;
    vec4 vOffsetScale; // xy: center of the tile in the atlas, zw: half its size, zero for a face that was not rendered
};

layout(std430, binding = 15) readonly buffer FogShadowFaces
{
    FogShadowFace_t g_FogShadowFaces[];
};

const int MAX_FOG_BARN_LIGHTS = 64;

uniform int g_nFogBarnLightCount;
uniform vec4 g_vFogBarnLight[MAX_FOG_BARN_LIGHTS]; // x: index in the barn light buffer, y: factor for the light's color, z: 1 when the fog shadows the light

// The fog density only exists inside a cube around the camera, and with 8 bit storage so does the baked light, in
// nested cubes. xyz: center of the cubes, w: half extent of the outermost one, zero or less for no limit
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

// How far into a volume a position is, from 0 at the edge to 1, by the shape of the volume
float GetVolumeFalloff(FogVolume_t volume, vec3 vUnit)
{
    vec3 vEdge = abs(vUnit * 2.0 - 1.0);
    int nShape = int(volume.vParams.x);

    if (nShape == 0)
    {
        return saturate(1.0 - max(vEdge.x, max(vEdge.y, vEdge.z)));
    }

    if (nShape == 1)
    {
        return saturate(1.0 - length(vEdge));
    }

    if (nShape == 2)
    {
        return min(saturate((1.0 - vUnit.z) * volume.vHeightParams.x), saturate((1.0 - max(vEdge.x, vEdge.y)) * volume.vHeightParams.y));
    }

    return 1.0;
}

// Density of the fog at a position: the strongest volume, and at least the floor. With 8 bit storage it is stored
// in 8 bits. The volumes also set the strength of the baked light and the tint.
float GetFogDensity(vec3 vPositionWs, out float flIndirectStrength, out vec3 vTint)
{
    bool bIs8Bit = g_nFogLightStorageIs8Bit != 0;

    flIndirectStrength = g_vFogTint_IndirectStrength.w;
    vTint = g_vFogTint_IndirectStrength.rgb;

    if (!IsInsideFogClipmap(vPositionWs))
    {
        return 0.0;
    }

    float flDensity = g_flFogDensityFloor;
    vec4 vPosition = vec4(vPositionWs, 1.0);

    for (int i = 0; i < g_nFogVolumeCount; i++)
    {
        FogVolume_t volume = g_FogVolumes[i];
        vec3 vUnit = vec3(dot(vPosition, volume.vUnitX), dot(vPosition, volume.vUnitY), dot(vPosition, volume.vUnitZ));

        if (any(lessThan(vUnit, vec3(0.0))) || any(greaterThan(vUnit, vec3(1.0))))
        {
            continue;
        }

        float flWeight = pow(GetVolumeFalloff(volume, vUnit), volume.vParams.z);

        if (flWeight <= 0.0)
        {
            continue;
        }

        flDensity = max(flDensity, flWeight * volume.vParams.y);

        if (volume.vParams.w >= 0.0)
        {
            flIndirectStrength = mix(flIndirectStrength, volume.vParams.w, flWeight);
        }

        if (volume.vHeightParams.z != 0.0)
        {
            vTint = mix(vTint, volume.vTint.rgb, flWeight);
        }
    }

    flDensity = saturate(flDensity);

    if (bIs8Bit)
    {
        flDensity = floor(flDensity * 255.0 + 0.5) / 255.0;
    }

    return flDensity;
}

// Distance along the view direction of a slice, by its position in the grid from 0 to 1
float GetSliceDepth(float flSlice)
{
    if (g_vFogDepthWarp.x > 0.0)
    {
        float flWarped = (pow(g_vFogDepthWarp.x + 1.0, flSlice) - 1.0) / g_vFogDepthWarp.x;
        return g_vFogNearRange.x + g_vFogNearRange.y * flWarped;
    }

    return g_vFogNearRange.x + g_vFogNearRange.y * flSlice * flSlice;
}

// The baked light of the fog at a position, which is zero outside of the controller's box
vec3 GetFogIndirectLighting(vec3 vPositionWs)
{
    return textureLod(g_tFogIrradiance, vPositionWs * g_vFogIrradianceScale + g_vFogIrradianceBias, 0.0).rgb;
}

// Unless the fog keeps its intermediate lighting as floats, the light clipmaps and slice lighting are stored as
// sqrt(light * 0.5) in 8 bits per channel, which caps the light they hold at 2.0.
// Lights that are bright close to their source, such as a lamp seen in haze, saturate here.
const float MAX_STORED_FOG_LIGHT = 2.0;

// How much of a light reaches a position through a tile of a shadow map. A position outside the tile is shadowed,
// unless it is clamped to the tile, as at the seams between the faces of a point light.
float GetFogShadowFromTile(sampler2DShadow shadowMap, mat4 matWorldToShadow, vec4 vOffsetScale, vec3 vPositionWs, bool bClampToTile)
{
    vec4 vClipPos = matWorldToShadow * vec4(vPositionWs, 1.0);

    if (vClipPos.w <= 0.0)
    {
        return 0.0;
    }

    vec3 vNdc = vClipPos.xyz / vClipPos.w;
    vec2 vTexelSize = 1.0 / vec2(textureSize(shadowMap, 0));

    // The tile is taken in by three texels, so the filter stays inside it
    vec2 vTileEdge = vec2(1.0) - 3.0 * vTexelSize / vOffsetScale.zw;

    if (vNdc.z < 0.0 || vNdc.z > 1.0)
    {
        return 0.0;
    }

    if (bClampToTile)
    {
        vNdc.xy = clamp(vNdc.xy, -vTileEdge, vTileEdge);
    }
    else if (any(greaterThan(abs(vNdc.xy), vTileEdge)))
    {
        return 0.0;
    }

    float flShadowRef = saturate(vNdc.z + SHADOW_RECEIVER_DEPTH_BIAS);

    return ComputeShadow_PCF_3x3_Gaussian(shadowMap, vNdc.xy * vOffsetScale.zw + vOffsetScale.xy, flShadowRef, vTexelSize);
}

// How much of a light reaches a position through its shadow map of the scene
float GetFogShadow(int nShadowSlot, vec3 vPositionWs)
{
    return GetFogShadowFromTile(g_tBarnLightShadowDepth, g_matLightWorldToShadow[nShadowSlot], g_vLightShadowOffsetScale[nShadowSlot], vPositionWs, false);
}

// How much of a light reaches a position through its faces in the fog shadow atlas. The face of a point light is the
// one along the largest axis of the offset to the position, and a face that was not rendered leaves the light unshadowed.
float GetFogAtlasShadow(FogLight_t light, int nFirstFace, vec3 vPositionWs)
{
    bool bIsSpot = light.vPosition_Spot.w != 0.0;
    int nFace = nFirstFace;

    if (!bIsSpot)
    {
        vec3 vOffset = vPositionWs - light.vPosition_Spot.xyz;
        vec3 vAbsOffset = abs(vOffset);
        int nAxis = 2;

        if (vAbsOffset.x >= vAbsOffset.y && vAbsOffset.x >= vAbsOffset.z)
        {
            nAxis = 0;
        }
        else if (vAbsOffset.y >= vAbsOffset.z)
        {
            nAxis = 1;
        }

        nFace += nAxis * 2;

        if (vOffset[nAxis] < 0.0)
        {
            nFace += 1;
        }
    }

    FogShadowFace_t face = g_FogShadowFaces[nFace];

    if (face.vOffsetScale.z == 0.0)
    {
        return 1.0;
    }

    return GetFogShadowFromTile(g_tFogShadowDepth, face.matWorldToShadow, face.vOffsetScale, vPositionWs, !bIsSpot);
}

// Light of one fog light at a position
vec3 GetFogLight(int i, vec3 vPositionWs)
{
    FogLight_t light = g_FogLights[i];
    vec3 vColor = light.vColor_InnerCos.rgb;

    vec3 vLightPositionWs = light.vPosition_Spot.xyz;
    vec3 vToLight = vLightPositionWs - vPositionWs;
    float flDistanceSquared = dot(vToLight, vToLight);

    if (flDistanceSquared > light.vFalloff.z)
    {
        return vec3(0.0);
    }

    float flClampedDistanceSquared = max(flDistanceSquared, 36.0);
    float flAttenuation = saturate(1.0 / dot(vec2(sqrt(flClampedDistanceSquared), flClampedDistanceSquared), light.vFalloff.xy) - light.vFalloff.w);

    if (flAttenuation <= 0.0)
    {
        return vec3(0.0);
    }

    int nStoreIndex = int(light.vParams.x);
    vec3 vCone = vec3(1.0);

    if (light.vPosition_Spot.w != 0.0)
    {
        float flCosAngle = dot(normalize(vToLight), -light.vDirection_OuterCos.xyz);
        float flOuterCos = light.vDirection_OuterCos.w;
        float flInnerCos = light.vColor_InnerCos.w;

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
    int nShadowFace = int(light.vParams.z);

    if (nShadowFace >= 0)
    {
        flShadow = GetFogAtlasShadow(light, nShadowFace, vPositionWs);

        if (flShadow <= 0.0)
        {
            return vec3(0.0);
        }
    }
    else if (nStoreIndex >= 0 && light.vParams.y != 0.0 && g_vLightShadowParams[nStoreIndex].x >= 0.0)
    {
        flShadow = GetFogShadow(int(g_vLightShadowParams[nStoreIndex].x), vPositionWs);

        if (flShadow <= 0.0)
        {
            return vec3(0.0);
        }
    }

    return vColor * flShadow * flAttenuation * vCone * flShadow;
}

// Light of one face of a barn light at a position
vec3 GetFogBarnLight(int i, vec3 vPositionWs)
{
    vec4 vParams = g_vFogBarnLight[i];
    BarnLightConstants barnLight = g_BarnLights[int(vParams.x)];

    vec3 vNdc;

    if (!GetBarnLightNdc(barnLight, vPositionWs, vNdc))
    {
        return vec3(0.0);
    }

    float flSkirtFade = GetBarnLightSkirtFade(barnLight, vNdc.z);

    if (flSkirtFade == 0.0)
    {
        return vec3(0.0);
    }

    vec3 vLightDir;
    float flAttenuation = GetBarnLightAttenuation(barnLight, vPositionWs, flSkirtFade, vLightDir);

    vec3 vLight = barnLight.m_vBarnLightColor_flCookie.xyz * (flAttenuation * vParams.y);
    vLight *= GetBarnLightCookie(barnLight, vNdc, vLightDir);

    if (vLight == vec3(0.0) || vParams.z == 0.0)
    {
        return vLight;
    }

    return vLight * GetBarnLightShadow(barnLight, vNdc);
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

    for (int i = 0; i < g_nFogBarnLightCount; i++)
    {
        vDynamicLighting += GetFogBarnLight(i, vPositionWs);
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
        float flDepth = GetSliceDepth(flSlice);
        vec3 vPositionWs = g_vCameraPositionWs + vRay * flDepth;

        float flIndirectStrength;
        vec3 vTint;
        float flDensity = GetFogDensity(vPositionWs, flIndirectStrength, vTint);
        vec3 vLighting = vec3(0.0);

        if (flDensity > 0.0)
        {
            vLighting = GetFogLighting(vPositionWs);

            if (flIndirectStrength > 0.0)
            {
                vLighting += GetFogIndirectLighting(vPositionWs) * flIndirectStrength;
            }

            vLighting *= vTint;
        }

        float flFadeIn = saturate(flDepth * g_vFogScatteringFadeIn.z + g_vFogScatteringFadeIn.w);
        float flExtinction = flDensity * (flDepth - flPreviousDepth) * flScattering * flFadeIn;

        vScatter += vLighting * flExtinction * saturate(exp(-flOpticalDepth));
        flOpticalDepth += flExtinction;
        flPreviousDepth = flDepth;

        imageStore(g_tFogVolumeOut, ivec3(vColumn, nSlice), vec4(vScatter, saturate(exp(-flOpticalDepth))));
    }
}
