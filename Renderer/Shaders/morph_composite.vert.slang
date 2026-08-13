#version 460

in vec4 vPOSITION;
in vec4 vTEXCOORD;
in vec4 vTEXCOORD1;
in vec4 vTEXCOORD2;

out vec4 offset;
out vec4 range;
out vec2 morphState;
out vec2 texCoords;

void main()
{
    offset = vTEXCOORD1;
    range = vTEXCOORD2;
    morphState = vec2(vPOSITION.zw);
    texCoords = vTEXCOORD.xy;
	
    vec4 pos = vec4(vPOSITION.xy, 1.0, 1.0);
    //pos.y = -vPOSITION.y;
    gl_Position = pos;
}
