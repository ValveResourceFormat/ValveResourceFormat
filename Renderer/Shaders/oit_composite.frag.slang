#version 460

// Composites the order independent transparency targets over the scene. Output is premultiplied.

#define D_MSAA 0

#if (D_MSAA == 1)
    layout(binding = 0) uniform sampler2DMS g_tOitAccumulation;
    layout(binding = 1) uniform sampler2DMS g_tOitRevealage;
    layout(binding = 2) uniform sampler2DMS g_tOitAdditive;
    #define FetchOit(tex) texelFetch(tex, ivec2(gl_FragCoord.xy), gl_SampleID)
#else
    layout(binding = 0) uniform sampler2D g_tOitAccumulation;
    layout(binding = 1) uniform sampler2D g_tOitRevealage;
    layout(binding = 2) uniform sampler2D g_tOitAdditive;
    #define FetchOit(tex) texelFetch(tex, ivec2(gl_FragCoord.xy), 0)
#endif

layout(location = 0) out vec4 outputColor;

void main()
{
    float revealage = FetchOit(g_tOitRevealage).r;
    vec3 additive = FetchOit(g_tOitAdditive).rgb;

    if (revealage >= 1.0 && all(equal(additive, vec3(0.0))))
    {
        discard;
    }

    vec4 accumulation = FetchOit(g_tOitAccumulation);

    if (isinf(max(max(accumulation.r, accumulation.g), accumulation.b)))
    {
        accumulation.rgb = vec3(accumulation.a);
    }

    vec3 averageColor = accumulation.rgb / max(accumulation.a, 1e-5);

    // Which covering fragments are in front of the additive light is unknown, so it is not dimmed
    outputColor = vec4(averageColor * (1.0 - revealage) + additive, 1.0 - revealage);
}
