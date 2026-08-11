#version 460

// Weighted combine of the active post process volumes' color correction LUTs into one texture.
layout(local_size_x = 4, local_size_y = 4, local_size_z = 4) in;

uniform vec4 g_vColorCorrectionWeights0;
uniform float g_flIdentityWeight;

uniform sampler3D g_tColorCorrection0;
uniform sampler3D g_tColorCorrection1;
uniform sampler3D g_tColorCorrection2;
uniform sampler3D g_tColorCorrection3;

layout(binding = 0, rgba8) uniform writeonly image3D g_tCombinedLut;

void main()
{
    ivec3 vCoord = ivec3(gl_GlobalInvocationID.xyz);
    ivec3 vDim = imageSize(g_tCombinedLut);

    if (any(greaterThanEqual(vCoord, vDim)))
    {
        return;
    }

    // What an identity LUT stores at this voxel
    vec3 vIdentity = (vec3(vCoord) + vec3(0.5)) / vec3(vDim);

    vec3 vColor = vIdentity * g_flIdentityWeight
        + texelFetch(g_tColorCorrection0, vCoord, 0).xyz * g_vColorCorrectionWeights0.x
        + texelFetch(g_tColorCorrection1, vCoord, 0).xyz * g_vColorCorrectionWeights0.y
        + texelFetch(g_tColorCorrection2, vCoord, 0).xyz * g_vColorCorrectionWeights0.z
        + texelFetch(g_tColorCorrection3, vCoord, 0).xyz * g_vColorCorrectionWeights0.w;

    imageStore(g_tCombinedLut, vCoord, vec4(vColor, 1.0));
}
