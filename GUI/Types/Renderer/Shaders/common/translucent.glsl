#version 460

#define F_ADDITIVE_BLEND 0

layout (location = 1) out float outputAlpha;

vec4 WeightColorTranslucency(vec4 color)
{
#if (F_ADDITIVE_BLEND == 1)
    outputAlpha = color.a;
    color.a = 0.01;
    return vec4(color.rgb * color.a, color.a);
#endif

    // https://casual-effects.blogspot.com/2014/03/weighted-blended-order-independent.html
    float depth = gl_FragCoord.z;
    float weight = max(min(1.0, max(max(color.r, color.g), color.b) * color.a), color.a)
        * clamp(0.03 / (1e-5 + pow(depth / 200, 4.0)), 1e-2, 3e3);

    outputAlpha = color.a;
    vec4 outputColor = vec4(color.rgb * color.a, color.a) * weight;

    return outputColor;
}
