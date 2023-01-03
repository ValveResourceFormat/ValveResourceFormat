#version 330

uniform float sceneObjectId = 0.0;

out vec4 outputColor;

void main()
{
    outputColor = vec4(sceneObjectId, .6, sceneObjectId, 1);
}
