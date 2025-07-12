#version 460

layout(triangles) in;
layout(triangle_strip, max_vertices = 21) out;

uniform float g_flLineSize = 0.2;

void main()
{
    const float flOutlineSize = g_flLineSize / 64.0;
    const float flNumIterations = clamp(g_flLineSize * 10.0, 3.0, 6.0);

    for (float i = 0.0; i <= float(nNumIterations); i += 1.0)
    {
        float fCycle = i / float(nNumIterations);

        vec2 vOffset = vec2(
            (sin(fCycle * fTwoPi)),
            (cos(fCycle * fTwoPi))
        );

        for (int j = 0; j < 3; j++)
        {
            vec2 vAspectRatio = normalize(g_vInvViewportSize);
		    gl_in[j].gl_Position.xy += (vOffset * 2.0) * vAspectRatio * gl_in[j].gl_Position.w * flOutlineSize;
        }

        gl_Position = gl_in[2].gl_Position; EmitVertex();
        gl_Position = gl_in[0].gl_Position; EmitVertex();
        gl_Position = gl_in[1].gl_Position; EmitVertex();
    }

    EndPrimitive();
}
