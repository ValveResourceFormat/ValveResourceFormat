#version 460

#ifndef VRF_FOLIAGE_VR_VS_INCLUDE
#define VRF_FOLIAGE_VR_VS_INCLUDE

// Foliage motion of vr_complex, by F_FOLIAGE_ANIMATION: 1 and 3 bend the trunk about its base, each branch about
// its painted pivot and each frond about a point behind its tip, 2 holds still, and 4 sways and flutters by height.
// Included after surface_base_vs.slang.

#include "foliage_wind_vs.slang"

#if (F_FOLIAGE_ANIMATION != 4)
    in vec4 vTEXCOORD3; // Frond flutter in x, frond facing in y, phase in z
    in vec4 vTEXCOORD4; // Branch pivot in object space
#endif

uniform float g_flTrunkDeflection = 0.0;
uniform float g_flTrunkDeflectionStart = 0.0;
uniform float g_flTrunkDeflectionEnd = 300.0;
uniform float g_flBranchDeflection = 0.0;
uniform float g_flBranchDeflectionStart = 0.0;
uniform float g_flBranchDeflectionEnd = 150.0;
uniform bool g_bBranchMotionPerpendicular = true;
uniform float g_flFrondFlutter = 0.0;
uniform float g_flFrondSpatialIncoherence = 0.1;
uniform float g_flFrondDeflectionStart = 0.0;
uniform float g_flFrondDeflectionEnd = 50.0;
uniform float g_flFrondPivotLength = 0.2;
uniform bool g_bJitterMotion = false;
uniform bool g_bUseObjectPivot = false;
uniform bool g_bFrondAnimationNormalFacing = false;

float FoliageWave(float t, float slowFrequency, float fastFrequency)
{
    return 0.5 * (sin(t * slowFrequency) + sin(t * fastFrequency));
}

float AddFoliageJitter(float wave, float time, float phase)
{
    if (!g_bJitterMotion)
    {
        return wave;
    }

    return wave + FoliageWave(time * (12.0 + fract(phase)), 0.71, 1.6) * g_flFrondFlutter * 0.2;
}

vec3 GetFoliageWindDirection(mat3x4 objectToWorld)
{
    return normalize((objectToWorld * g_vWindDirection.xyz).xyz + vec3(0.0, 0.0001, 0.0));
}

#if (F_FOLIAGE_ANIMATION == 1) || (F_FOLIAGE_ANIMATION == 3)

void AnimateFoliageDeflection(mat3x4 objectToWorld, inout vec3 position, inout vec3 normal)
{
    vec3 foliageParams = vTEXCOORD3.xyz;
    vec3 pivotPaint = vTEXCOORD4.xyz;

    vec3 objectOrigin = vec4(0.0, 0.0, 0.0, 1.0) * objectToWorld;
    float objectPhase = fract((objectOrigin.x + objectOrigin.y + objectOrigin.z) * 0.1);
    float pivotPhase = fract(foliageParams.z + pivotPaint.x + pivotPaint.y + pivotPaint.z + objectPhase);
    float frondFacing = saturate(foliageParams.y * 1.05 - 0.025);

    vec3 axisScales = vec3(
        length(vec3(objectToWorld[0].x, objectToWorld[1].x, objectToWorld[2].x)),
        length(vec3(objectToWorld[0].y, objectToWorld[1].y, objectToWorld[2].y)),
        length(vec3(objectToWorld[0].z, objectToWorld[1].z, objectToWorld[2].z)));
    float objectScale = sqrt(max(0.5, max(axisScales.x, max(axisScales.y, axisScales.z))));

    float windStrength = length(g_vWindDirection.xyz) * g_vWindStrengthFreqMulHighStrength.x / objectScale;
    vec3 windDirection = GetFoliageWindDirection(objectToWorld);
    vec3 windSide = cross(windDirection, vec3(0.0, 0.0, 1.0));
    vec3 trunkBase = cross(windSide, windDirection) * g_flTrunkDeflectionStart;

    float trunkStrength = g_flTrunkDeflection * windStrength;
    float trunkTime = g_flTime + objectPhase;
    float trunkMotion = 0.0;
    vec3 pivot = pivotPaint;

    if (trunkStrength > 0.0)
    {
        vec3 fromBase = position - trunkBase;

        float wave = AddFoliageJitter(FoliageWave(trunkTime / objectScale, 0.71, 1.6), trunkTime, objectPhase);
        wave = mix(wave, wave * 0.25 - 0.5, g_vWindStrengthFreqMulHighStrength.x);

        float angle = clamp((length(fromBase) - g_flTrunkDeflectionStart) / g_flTrunkDeflectionEnd, 0.0, 1.0) * trunkStrength * wave;
        float axisTime = trunkTime * (0.25 / objectScale);
        vec3 axis = normalize(mix(windDirection, windSide, vec3(FoliageWave(axisTime, 0.71, 1.6) * 0.5 + 0.5)));
        vec4 rotation = vec4(axis * sin(angle), cos(angle));

        position = RotateByFoliageQuaternion(fromBase, rotation) + trunkBase;
        pivot = RotateByFoliageQuaternion(pivot - trunkBase, rotation) + trunkBase;
        normal = normalize(RotateByFoliageQuaternion(normal, rotation));
        trunkMotion = abs(wave * trunkStrength);
    }

    vec3 frondAxis = vec3(0.0, 1.0, 0.0);
    float motion = trunkMotion;

    // A zero pivot means the vertex is on the trunk
    if (length(pivotPaint) != 0.0)
    {
        vec3 branchSide = cross(windDirection, normalize(pivot - trunkBase));
        vec3 branchForward = cross(branchSide, windDirection);
        vec3 branchAxis = g_bBranchMotionPerpendicular ? branchForward : branchSide;
        frondAxis = g_bBranchMotionPerpendicular ? branchSide : branchForward;

        float branchStrength = g_flBranchDeflection * windStrength;
        branchStrength += branchStrength * trunkMotion;

        if (branchStrength > 0.0)
        {
            vec3 fromPivot = position - pivot;
            float distanceAlongBranch = max(0.0, length(fromPivot) - g_flBranchDeflectionStart) / g_flBranchDeflectionEnd;
            float branchTime = g_flTime + pivotPhase - sqrt(max(1.0, distanceAlongBranch));

            float wave = AddFoliageJitter(FoliageWave(branchTime, 2.84, 6.4), g_flTime + pivotPhase, pivotPhase);
            float angle = branchStrength * wave;
            vec4 rotation = vec4(branchAxis * sin(angle), cos(angle));

            position = RotateByFoliageQuaternion(fromPivot, rotation) + pivot;
            normal = normalize(RotateByFoliageQuaternion(normal, rotation));
            motion = trunkMotion + abs(wave * branchStrength);
        }
    }

    // Fronds turn about a point behind their tip, along the branch or facing out of the leaf
    vec3 fromPivot = position - pivot;
    float distanceFromPivot = length(fromPivot);
    vec3 directionFromPivot = normalize(fromPivot);
    vec3 frondDirection = normalize(mix(directionFromPivot, cross(normal, cross(directionFromPivot, normal)), vec3(frondFacing)));
    vec3 frondPivot = position - frondDirection * g_flFrondPivotLength;
    vec3 fromFrondPivot = position - frondPivot;

    float frondPhase = fract(foliageParams.z + pivotPhase + (vPOSITION.x + vPOSITION.y) * g_flFrondSpatialIncoherence * frondFacing);
    float frondTime = (g_flTime + frondPhase - sqrt(max(1.0, length(fromFrondPivot) * 0.001))) * (12.0 + pivotPhase);

    float flutter = g_flFrondFlutter * saturate(0.75 + motion * 0.25) * saturate(foliageParams.x);
    flutter *= 1.0 + 2.0 * sqrt(abs(dot(normalize(normal), windDirection)) * frondFacing);
    flutter *= windStrength / objectScale;
    flutter *= clamp((distanceFromPivot - g_flFrondDeflectionStart) / g_flFrondDeflectionEnd, 0.0, 1.0);
    flutter *= saturate(max(distanceFromPivot, g_flTrunkDeflectionEnd) / g_flTrunkDeflectionEnd);

    float angle = flutter * FoliageWave(frondTime, 0.71, 1.6);
    vec3 axis = mix(frondAxis, cross(windDirection, normal), vec3(frondFacing));
    vec4 rotation = vec4(axis * sin(angle), cos(angle));

    position = RotateByFoliageQuaternion(fromFrondPivot, rotation) + frondPivot;
    normal = normalize(normal);
}

#elif (F_FOLIAGE_ANIMATION == 4)

void AnimateFoliageFronds(mat3x4 objectToWorld, inout vec3 position, vec3 normal)
{
    vec3 positionWs = vec4(position, 1.0) * objectToWorld;
    vec3 windDirection = GetFoliageWindDirection(objectToWorld);

    vec3 pivot = g_bUseObjectPivot ? vec3(0.0) : vec3(position.xy, 0.0);
    float phase = g_bUseObjectPivot ? 0.0 : fract(position.x + position.y);

    float deflection = clamp((abs(position.z) - g_flFrondDeflectionStart) / g_flFrondDeflectionEnd, 0.0, 1.0);

    if (g_bFrondAnimationNormalFacing)
    {
        deflection *= 1.0 - abs(dot(normalize(normal), windDirection));
    }

    float swayTime = g_flTime + 0.5 * (positionWs.x + positionWs.y) * g_flFrondSpatialIncoherence;
    float sway = FoliageWave(swayTime, 2.13, 4.8) * length(g_vWindDirection.xyz) * g_vWindStrengthFreqMulHighStrength.x;

    vec3 fromPivot = position + windDirection * sway * deflection - pivot;
    float flutterTime = (g_flTime + phase - sqrt(max(1.0, length(fromPivot) * 0.001))) * (12.0 + phase);

    float angle = deflection * g_flFrondFlutter * sway * FoliageWave(flutterTime, 0.71, 1.6);
    vec4 rotation = vec4(cross(windDirection, vec3(0.0, 0.0, 1.0)) * sin(angle), cos(angle));

    position = RotateByFoliageQuaternion(fromPivot, rotation) + pivot;
}

#endif

// Returns the world position, and writes the normal that follows the motion to vNormalOut
vec3 AnimateVrFoliage()
{
    mat3x4 objectToWorld = CalculateObjectToWorldMatrix();

    vec3 position = vPOSITION;
    vec3 normal;
    vec4 tangent;
    GetOptionallyCompressedNormalTangent(normal, tangent);

#if (F_FOLIAGE_ANIMATION == 1) || (F_FOLIAGE_ANIMATION == 3)
    AnimateFoliageDeflection(objectToWorld, position, normal);
#elif (F_FOLIAGE_ANIMATION == 4)
    AnimateFoliageFronds(objectToWorld, position, normal);
#endif

    vNormalOut = normalize(vec4(normal, 0.0) * objectToWorld);
    return vec4(position, 1.0) * objectToWorld;
}

#endif
