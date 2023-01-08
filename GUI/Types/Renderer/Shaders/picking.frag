#version 330

uniform uint sceneObjectId = uint(1);

out uint outputColor;

void main()
{
    outputColor = uint(sceneObjectId);
}
