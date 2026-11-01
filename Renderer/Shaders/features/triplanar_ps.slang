#version 460

#ifndef VRF_TRIPLANAR_PS_INCLUDE
#define VRF_TRIPLANAR_PS_INCLUDE

// Textures projected along the X, Y and Z world axes, weighted by how squarely the surface faces each axis.

struct Triplanar_t
{
    vec2 TexCoords[3];
    vec2 TexCoordsDdx[3];
    vec2 TexCoordsDdy[3];
    vec3 Weights;
};

Triplanar_t GetTriplanar(vec3 positionWs, vec3 normal, float scale)
{
    vec3 position = positionWs / scale;

    Triplanar_t triplanar;
    triplanar.TexCoords[0] = -position.yz;
    triplanar.TexCoords[1] = position.xz;
    triplanar.TexCoords[2] = position.xy;

    // Derivatives are taken before any branching on the weights
    for (int axis = 0; axis < 3; axis++)
    {
        triplanar.TexCoordsDdx[axis] = dFdx(triplanar.TexCoords[axis]);
        triplanar.TexCoordsDdy[axis] = dFdy(triplanar.TexCoords[axis]);
    }

    vec3 weights = pow(abs(normal), vec3(16.0));
    triplanar.Weights = weights / dot(weights, vec3(1.0));

    return triplanar;
}

vec4 SampleTriplanarAxis(sampler2D tex, Triplanar_t triplanar, int axis)
{
    if (triplanar.Weights[axis] <= 0.0)
    {
        return vec4(0.0);
    }

    return textureGrad(tex, triplanar.TexCoords[axis], triplanar.TexCoordsDdx[axis], triplanar.TexCoordsDdy[axis]);
}

vec4 SampleTriplanar(sampler2D tex, Triplanar_t triplanar)
{
    vec4 sampleX = SampleTriplanarAxis(tex, triplanar, 0);
    vec4 sampleY = SampleTriplanarAxis(tex, triplanar, 1);
    vec4 sampleZ = SampleTriplanarAxis(tex, triplanar, 2);

    return sampleX * triplanar.Weights.x + sampleY * triplanar.Weights.y + sampleZ * triplanar.Weights.z;
}

// Tilts the surface normal by a hemi-octahedron normal map projected along each axis, roughness is read from blue.
vec3 SampleTriplanarNormal(sampler2D tex, Triplanar_t triplanar, vec3 normal, out float roughness)
{
    vec4 normalTextureX = SampleTriplanarAxis(tex, triplanar, 0);
    vec4 normalTextureY = SampleTriplanarAxis(tex, triplanar, 1);
    vec4 normalTextureZ = SampleTriplanarAxis(tex, triplanar, 2);

    vec3 weights = triplanar.Weights;
    roughness = normalTextureX.b * weights.x + normalTextureY.b * weights.y + normalTextureZ.b * weights.z;

    vec3 normalX = DecodeHemiOctahedronNormal(normalTextureX.rg);
    vec3 normalY = DecodeHemiOctahedronNormal(normalTextureY.rg);
    vec3 normalZ = DecodeHemiOctahedronNormal(normalTextureZ.rg);

    // Each projection adds the decoded x and y to two world axes, the X projection uses X and Z rather than Y and Z
    vec3 tilt = vec3(-normalX.x, 0.0, -normalX.y) * weights.x
        + vec3(normalY.x, 0.0, normalY.y) * weights.y
        + vec3(normalZ.x, normalZ.y, 0.0) * weights.z;

    return normalize(normal + tilt);
}

#endif
