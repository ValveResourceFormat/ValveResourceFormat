#version 460

#ifndef VRF_FOLIAGE_DEFLECTION_VS_INCLUDE
#define VRF_FOLIAGE_DEFLECTION_VS_INCLUDE

// Trunk and branch deflection of trees around painted pivots, with frond tip noise. Included after surface_base_vs.slang.

#include "foliage_wind_vs.slang"

in vec4 vTEXCOORD3; // Frond tip mask in red, branch stiffness in green
in vec4 vTEXCOORD4; // Branch pivot in object space

uniform float g_flTrunkDeflection = 0.0;
uniform float g_flTrunkDeflectionStart = 0.0;
uniform float g_flTrunkDeflectionEnd = 300.0;
uniform float g_flTrunkNoiseLevel = 0.5;
uniform float g_flTrunkAnimSpeed = 1.0;
uniform float g_flBranchDeflection = 0.0;
uniform float g_flBranchDeflectionStart = 0.0;
uniform float g_flBranchDeflectionEnd = 150.0;
uniform float g_flBranchNoiseLevel = 0.5;
uniform float g_flBranchAnimSpeed = 1.0;
uniform bool g_bBranchMotionPerpendicular = true;
uniform float g_flFrondTipStrength = 2.0;
uniform float g_flFrondTipMaskFalloff = 1.0;
uniform float g_flFrondTipAnimSpeed = 1.0;
uniform float g_flFrondTipSpatialIncoherence = 1.0;
uniform bool g_bFrondTipUseEnvWind = false;
uniform vec3 g_vNoiseUvScale = vec3(1.0);
uniform vec3 g_vNoiseUvSpeed = vec3(0.25);
uniform bool g_bSmootherNoiseBranchAndTrunk = true;

// Rotates by twice the angle whose sine scales q.xyz and whose cosine is q.w
vec3 RotateByFoliageQuaternion(vec3 v, vec4 q)
{
    vec3 conjugateAxis = -q.xyz;
    vec3 halfXyz = v * q.w + cross(v, conjugateAxis);
    float halfW = -dot(v, conjugateAxis);
    return halfXyz * q.w + q.xyz * halfW + cross(q.xyz, halfXyz);
}

float GetDeflectionWave(float t, vec3 noise, float noiseLevel)
{
    float slowWave = sin(t * 0.71);
    float fastWave = sin(t * 1.6);

    if (!g_bSmootherNoiseBranchAndTrunk)
    {
        float noisyWave = mix(mix(slowWave, fastWave, noise.x), sin(t), noise.y) * 1.5;
        return mix(0.5 * (slowWave + fastWave), noisyWave, noiseLevel);
    }

    float slowWeight = mix(0.5, noise.x * 0.95 + 0.05, noiseLevel);
    float fastWeight = mix(0.5, noise.y * 0.95 + 0.05, noiseLevel);
    float baseWeight = mix(0.0, noise.z * 0.95 + 0.05, noiseLevel);
    float weightSum = max(slowWeight + fastWeight + baseWeight, 0.01);

    // The base wave is not normalized by the weight sum
    float wave = slowWave * (slowWeight / weightSum) + fastWave * (fastWeight / weightSum) + sin(t) * baseWeight;
    return wave * mix(1.0, 1.5, noiseLevel);
}

vec3 SampleDeflectionNoise(vec3 position, float animSpeed)
{
    vec3 noiseCoords = position * (g_vNoiseUvScale * 0.001) + g_vNoiseUvSpeed * (g_flTime * 0.1 * animSpeed);
    return textureLod(g_tNoiseMap, noiseCoords, 0.0).xyz;
}

// Bends the whole plant about its base, then each branch about its pivot, in object space.
// Returns the world position, and writes the normal that follows the bending to vNormalOut. The tangent is left as it was.
vec3 AnimateFoliageDeflection()
{
    mat3x4 objectToWorld = CalculateObjectToWorldMatrix();

    vec3 position = vPOSITION;
    vec3 pivot = vTEXCOORD4.xyz;
    vec4 foliageParams = vTEXCOORD3;

    vec3 normal;
    vec4 tangent;
    GetOptionallyCompressedNormalTangent(normal, tangent);

    vec3 objectOrigin = vec4(0.0, 0.0, 0.0, 1.0) * objectToWorld;
    float objectPhase = fract((objectOrigin.x + objectOrigin.y + objectOrigin.z) * 0.1);
    float pivotPhase = fract(pivot.x + pivot.y + pivot.z + objectPhase);

    vec3 axisScales = vec3(
        length(vec3(objectToWorld[0].x, objectToWorld[1].x, objectToWorld[2].x)),
        length(vec3(objectToWorld[0].y, objectToWorld[1].y, objectToWorld[2].y)),
        length(vec3(objectToWorld[0].z, objectToWorld[1].z, objectToWorld[2].z)));
    float objectScale = sqrt(max(0.5, max(axisScales.x, max(axisScales.y, axisScales.z))));

    float rawWindStrength = g_vWindStrengthFreqMulHighStrength.x;
    float windStrength = length(g_vWindDirection.xyz) * rawWindStrength / objectScale;
    vec3 windDirection = normalize((objectToWorld * g_vWindDirection.xyz).xyz + vec3(0.0, 0.0001, 0.0));
    vec3 windSide = cross(windDirection, vec3(0.0, 0.0, 1.0));

    // Trunk
    vec3 trunkBase = cross(windSide, windDirection) * g_flTrunkDeflectionStart;
    float trunkSpeed = g_flTrunkAnimSpeed / objectScale;
    float trunkStrength = g_flTrunkDeflection * windStrength;
    float trunkMotion = 0.0;

    if (trunkStrength > 0.0)
    {
        float heightAlongTrunk = clamp((length(position - trunkBase) - g_flTrunkDeflectionStart) / g_flTrunkDeflectionEnd, 0.0, 1.0);
        float trunkBend = heightAlongTrunk * trunkStrength;

        vec3 noise = SampleDeflectionNoise(trunkBase, g_flTrunkAnimSpeed);
        float wave = GetDeflectionWave((g_flTime + trunkBend) * trunkSpeed, noise, saturate(g_flTrunkNoiseLevel));
        wave = mix(wave, wave * 0.25 - 0.5, rawWindStrength);

        float axisTime = (g_flTime + objectPhase) * (trunkSpeed * 0.25);
        float axisBlend = (sin(axisTime * 0.71) + sin(axisTime * 1.6)) * 0.25 + 0.5;
        vec3 axis = normalize(mix(windDirection, windSide, vec3(axisBlend)));

        float angle = trunkBend * wave;
        vec4 rotation = vec4(axis * sin(angle), cos(angle));

        normal = normalize(RotateByFoliageQuaternion(normal, rotation));
        position = RotateByFoliageQuaternion(position - trunkBase, rotation) + trunkBase;
        pivot = RotateByFoliageQuaternion(pivot - trunkBase, rotation) + trunkBase;
        trunkMotion = abs(wave * trunkStrength);
    }

    // Branches, a zero pivot means the vertex is on the trunk
    if (length(vTEXCOORD4.xyz) != 0.0)
    {
        vec3 axis = cross(windDirection, normalize(pivot - trunkBase));
        if (g_bBranchMotionPerpendicular)
        {
            axis = cross(axis, windDirection);
        }

        float branchStrength = g_flBranchDeflection * windStrength * (foliageParams.y + 0.25);
        branchStrength += branchStrength * trunkMotion;

        if (branchStrength > 0.0)
        {
            vec3 noise = SampleDeflectionNoise(pivot, g_flBranchAnimSpeed);
            float distanceAlongBranch = max(0.0, length(position - pivot) - g_flBranchDeflectionStart) / g_flBranchDeflectionEnd;
            float branchPhase = pivotPhase - sqrt(max(1.0, distanceAlongBranch));
            float wave = GetDeflectionWave((g_flTime + branchPhase) * (2.0 * g_flBranchAnimSpeed), noise, saturate(g_flBranchNoiseLevel));

            float angle = branchStrength * wave;
            vec4 rotation = vec4(axis * sin(angle), cos(angle));

            normal = normalize(RotateByFoliageQuaternion(normal, rotation));
            position = RotateByFoliageQuaternion(position - pivot, rotation) + pivot;
        }
    }

    // Frond tips
    float frondTipMask = pow(saturate(foliageParams.x), g_flFrondTipMaskFalloff);
    vec3 restPositionWs = vec4(vPOSITION, 1.0) * objectToWorld;
    float frondTime = g_flTime * (4.0 + pivotPhase * 0.25) * 0.05 * g_flFrondTipAnimSpeed;
    vec3 frondNoiseCoords = restPositionWs * (g_vNoiseUvScale * 0.001) * g_flFrondTipSpatialIncoherence + g_vNoiseUvSpeed * frondTime;
    vec3 frondNoise = SampleSignedWindNoise(frondNoiseCoords);

    vec3 frondOffset = frondNoise * g_flFrondTipStrength * frondTipMask;
    if (g_bFrondTipUseEnvWind)
    {
        frondOffset *= WindAlignment(frondNoise) * rawWindStrength;
    }

    vNormalOut = normalize(vec4(normal, 0.0) * objectToWorld);
    return vec4(position + frondOffset, 1.0) * objectToWorld;
}

#endif
