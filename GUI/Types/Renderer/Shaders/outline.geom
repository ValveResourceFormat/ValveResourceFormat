#version 460

layout(triangles) in;
layout(triangle_strip, max_vertices = 21) out;

uniform float g_flLineSize = 0.2;

void main()
{
    const float flOutlineSize = g_flLineSize / 64.0;
    const float flNumIterations = clamp(g_flLineSize * 10.0, 3.0, 6.0);
    const float fTwoPi = 6.28318;

    #define g_vInvViewportSize (vec2(1.0) / vec2(1024.0, 1024.0))

    for (float i = 0.0; i <= flNumIterations; i += 1.0)
    {
        float fCycle = i / flNumIterations;

        vec2 vOffset = vec2(
            (sin(fCycle * fTwoPi)),
            (cos(fCycle * fTwoPi))
        );

        vec4 vExpandedPositionPs[3];

        for (int j = 0; j < 3; j++)
        {
            vec2 vAspectRatio = normalize(g_vInvViewportSize);
            vExpandedPositionPs[j].xy = gl_in[j].gl_Position.xy + (vOffset * 2.0) * vAspectRatio * gl_in[j].gl_Position.w * flOutlineSize;
        }

        gl_Position = vExpandedPositionPs[2]; EmitVertex();
        gl_Position = vExpandedPositionPs[0]; EmitVertex();
        gl_Position = vExpandedPositionPs[1]; EmitVertex();
    }

    EndPrimitive();
}
