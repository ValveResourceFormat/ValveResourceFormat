#version 460

// Clutter instance culling compute shader.
// Unpacks clutter instance data and generates indirect draw arguments for visible instances.

#include "common/ViewConstants.slang"
#include "common/structs.slang"

layout (local_size_x = 64) in;

// Clutter instance data from CPU
struct ClutterInstanceData
{
    vec3 position;
    float scale;
    uint orientation32;
    uint tintSrgb;
};

// Output transform and instance data
struct DrawTransform
{
    mat3x4 transform;
};

struct DrawInstance
{
    uint tintAlpha;
    uint transformIndex;
    uint visibleLPV;
    uint identification;
    uvec4 envMapVisibility;
};

// Input: Packed clutter instance data
layout(std430, binding = 5) readonly buffer ClutterInstanceBuffer
{
    ClutterInstanceData clutterInstances[];
};

// Output: Transforms for visible instances
layout(std430, binding = 6) writeonly buffer TransformBuffer
{
    DrawTransform instanceTransforms[];
};

// Output: Per-instance data for visible instances
layout(std430, binding = 7) writeonly buffer InstanceBuffer
{
    DrawInstance instanceData[];
};

// Output: Indirect draw arguments
layout(std430, binding = 8) buffer IndirectDrawBuffer
{
    DrawCommand indirectDraws[];
};

// Clutter parameters
uniform uint g_nTotalInstances;
uniform uint g_nDrawCommandIndex;
uniform uint g_nBaseTransformIndex;
uniform uint g_nBaseInstanceIndex;
uniform uint g_nIndexCount;
uniform uint g_nFirstIndex;
uniform int g_nBaseVertex;
uniform float g_flBeginCullSize;
uniform float g_flEndCullSize;
uniform float g_flModelRadius; // Bounding sphere radius of the instanced model

// Unpack smallest-three quaternion from 32-bit packed value
vec4 UnpackOrientation32(uint packed)
{
    uint largestIndex = packed & 0x3u;
    uint a10 = (packed >> 2) & 0x3FFu;
    uint b10 = (packed >> 12) & 0x3FFu;
    uint c10 = (packed >> 22) & 0x3FFu;
    
    // Convert 10-bit unsigned to signed [-511, 511]
    int aSigned = int(a10);
    int bSigned = int(b10);
    int cSigned = int(c10);
    
    if (aSigned >= 512) aSigned -= 1024;
    if (bSigned >= 512) bSigned -= 1024;
    if (cSigned >= 512) cSigned -= 1024;
    
    // Normalize to [-1, 1]
    float a = float(aSigned) / 511.0;
    float b = float(bSigned) / 511.0;
    float c = float(cSigned) / 511.0;
    
    // Reconstruct the largest component
    float sumSquares = a * a + b * b + c * c;
    float d = sqrt(max(0.0, 1.0 - sumSquares));
    
    // Reconstruct quaternion based on which component was largest
    if (largestIndex == 0u) return vec4(d, a, b, c);
    if (largestIndex == 1u) return vec4(a, d, b, c);
    if (largestIndex == 2u) return vec4(a, b, d, c);
    return vec4(a, b, c, d);
}

// Convert quaternion to 3x3 rotation matrix
mat3 QuaternionToMatrix(vec4 q)
{
    float xx = q.x * q.x;
    float yy = q.y * q.y;
    float zz = q.z * q.z;
    float xy = q.x * q.y;
    float xz = q.x * q.z;
    float yz = q.y * q.z;
    float wx = q.w * q.x;
    float wy = q.w * q.y;
    float wz = q.w * q.z;
    
    return mat3(
        1.0 - 2.0 * (yy + zz), 2.0 * (xy + wz), 2.0 * (xz - wy),
        2.0 * (xy - wz), 1.0 - 2.0 * (xx + zz), 2.0 * (yz + wx),
        2.0 * (xz + wy), 2.0 * (yz - wx), 1.0 - 2.0 * (xx + yy)
    );
}

// Calculate fade alpha based on screen space size (returns 0-1, or -1 if culled)
float CalculateFadeAlpha(vec3 worldPos, float scale)
{
    if (g_flEndCullSize <= 0.0)
    {
        return 1.0; // Size culling disabled, full opacity
    }
    
    // Project position to clip space
    vec4 clipPos = g_matWorldToProjection * vec4(worldPos, 1.0);
    
    if (clipPos.w <= 0.0)
    {
        return -1.0; // Behind camera, cull
    }
    
    // Calculate world-space radius of this instance
    float worldRadius = g_flModelRadius * scale;
    
    // Calculate screen-space size using perspective projection
    // Project the radius into screen space at this distance
    float distance = length(worldPos - g_vCameraPositionWs);
    
    if (distance <= 0.0001)
    {
        return 1.0; // Very close, full opacity
    }
    
    // Screen-space radius in pixels (using perspective projection)
    // radius_screen = radius_world * focal_length / distance
    // focal_length approximated using viewport height and FOV
    float screenRadius = (worldRadius * g_vViewportSize.y) / (distance * 2.0);
    float screenSize = screenRadius * 2.0; // Diameter in pixels
    
    if (screenSize < g_flEndCullSize)
    {
        return -1.0; // Too small, cull
    }
    
    // Fade from 0 to 1 between EndCullSize and BeginCullSize
    if (screenSize < g_flBeginCullSize)
    {
        float fade = (screenSize - g_flEndCullSize) / (g_flBeginCullSize - g_flEndCullSize);
        return clamp(fade, 0.0, 1.0);
    }
    
    return 1.0; // Full opacity
}

void main()
{
    uint instanceIndex = gl_GlobalInvocationID.x;
    
    if (instanceIndex >= g_nTotalInstances)
    {
        return;
    }
    
    ClutterInstanceData instance = clutterInstances[instanceIndex];
    
    // Calculate fade alpha and test culling
    float fadeAlpha = CalculateFadeAlpha(instance.position, instance.scale);
    if (fadeAlpha < 0.0)
    {
        return; // Instance culled
    }
    
    // Unpack orientation quaternion and convert to rotation matrix
    vec4 quat = UnpackOrientation32(instance.orientation32);
    mat3 rotation = QuaternionToMatrix(quat);
    
    // Build transform matrix (rotation + scale + translation)
    mat3x4 transform;
    transform[0] = vec4(rotation[0] * instance.scale, instance.position.x);
    transform[1] = vec4(rotation[1] * instance.scale, instance.position.y);
    transform[2] = vec4(rotation[2] * instance.scale, instance.position.z);
    
    // Write transform to buffer
    uint transformIndex = g_nBaseTransformIndex + instanceIndex;
    instanceTransforms[transformIndex].transform = transform;
    
    // Unpack tint color (RGB only, ignore source alpha) and apply fade alpha
    uint tintRgb = instance.tintSrgb & 0x00FFFFFFu; // Mask out alpha
    uint alphaChannel = uint(fadeAlpha * 255.0) << 24; // Convert fade to 0-255 and shift to alpha position
    uint tintAlpha = tintRgb | alphaChannel;
    
    // Write instance data
    uint instanceDataIndex = g_nBaseInstanceIndex + instanceIndex;
    instanceData[instanceDataIndex].tintAlpha = tintAlpha;
    instanceData[instanceDataIndex].transformIndex = transformIndex;
    instanceData[instanceDataIndex].visibleLPV = 0u; // TODO: Light probe volume
    instanceData[instanceDataIndex].identification = 0u; // TODO: Selection ID
    instanceData[instanceDataIndex].envMapVisibility = uvec4(0u); // TODO: Env map visibility
}

