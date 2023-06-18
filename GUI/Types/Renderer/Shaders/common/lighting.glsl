
uniform mat4 vLightPosition;
uniform vec4 vLightColor;

vec3 getSunDir()
{
    return -normalize(mat3(vLightPosition) * vec3(-1, 0, 0));
}

vec3 getSunColor()
{
    return vLightColor.rgb; //pow(vLightColor.rgb, vec3(2.2)) * pow(vLightColor.a, 0.5);
}
