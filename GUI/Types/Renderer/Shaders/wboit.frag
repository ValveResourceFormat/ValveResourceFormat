#version 460

layout (location = 0) out vec4 frag;

layout (location = 0) uniform  sampler2DMS colorTexture;
layout (location = 1) uniform  sampler2DMS alphaTexture;

// epsilon number
const float EPSILON = 0.00001f;

// calculate floating point numbers equality accurately
bool isApproximatelyEqual(float a, float b)
{
    return abs(a - b) <= (abs(a) < abs(b) ? abs(b) : abs(a)) * EPSILON;
}

// get the max value between three values
float max3(vec3 v)
{
    return max(max(v.x, v.y), v.z);
}

void main()
{
    // fragment coordination
    ivec2 coords = ivec2(gl_FragCoord.xy);

    // fragment revealage
    float revealage = texelFetch(alphaTexture, coords, gl_SampleID).r;

    // save the blending and color texture fetch cost if there is not a transparent fragment
    if (isApproximatelyEqual(revealage, 1.0))
        discard;

    // fragment color
    vec4 accumulation = texelFetch(colorTexture, coords, gl_SampleID);

    // suppress overflow
    if (isinf(max3(abs(accumulation.rgb))))
        discard;

    // prevent floating point precision bug
    vec3 average_color = accumulation.rgb / max(accumulation.a, EPSILON);

    // blend pixels
    frag = vec4(average_color.rgb, 1.0 - revealage);

    //frag.rgb = revealage.xxx;
    // frag.a = 1.0 - revealage;
    //frag.a = 1.0;
}
