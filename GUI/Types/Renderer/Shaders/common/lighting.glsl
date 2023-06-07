
uniform mat4 vLightPosition;

vec3 getSunDir()
{
    return -normalize(mat3(vLightPosition) * vec3(-1, 0, 0));
}
