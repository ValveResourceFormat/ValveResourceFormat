#version 460

#define F_ADDITIVE_BLEND 0

layout (location = 1) out float outputAlpha;

vec4 WeightColorTranslucency(vec4 color)
{
    float depth = gl_FragCoord.z;
    depth = pow(depth, 1);
    float units_1k = 1000 / gl_DepthRange.diff;
    float weight = (depth - gl_DepthRange.near) * units_1k;
    weight = clamp(weight, 0.0, 0.3);
    weight = pow(weight, 3);

#if (F_ADDITIVE_BLEND == 1)
    outputAlpha = weight;
    return vec4(color.rgb * color.a, outputAlpha);
#endif

    outputAlpha = color.a;
    vec4 outputColor = vec4(color.rgb * color.a, color.a) * weight;

    // outputColor.rgb = weight.xxx;
    // outputColor.a = 1.0;

    return outputColor;
}
