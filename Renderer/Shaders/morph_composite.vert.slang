#version 460

// Locations and names pair with the VertexFormat in MorphComposite.cs.
layout (location = 0) in vec4 vPositionWeights;
layout (location = 1) in vec4 vTexCoords;
layout (location = 2) in vec4 vOffsetsPositionSpeed;
layout (location = 3) in vec4 vRangesPositionSpeed;

out vec4 offset;
out vec4 range;
out vec2 morphState;
out vec2 texCoords;

void main()
{
    offset = vOffsetsPositionSpeed;
    range = vRangesPositionSpeed;
    morphState = vec2(vPositionWeights.zw);
    texCoords = vTexCoords.xy;
	
    vec4 pos = vec4(vPositionWeights.xy, 1.0, 1.0);
    //pos.y = -vPositionWeights.y;
    gl_Position = pos;
}
