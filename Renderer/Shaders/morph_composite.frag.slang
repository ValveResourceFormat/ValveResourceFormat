#version 460

uniform sampler2D morphAtlas;
uniform int bCompositeNormals;

in vec4 offsetsPositionSpeed;
in vec4 rangesPositionSpeed;
in vec4 offsetsNormalWrinkle;
in vec4 rangesNormalWrinkle;
in vec2 weights;
in vec4 texCoords;

out vec4 outputColor;

void main()
{
    vec4 positionSpeed = (texture(morphAtlas, texCoords.xy) * rangesPositionSpeed) + offsetsPositionSpeed;

    // Speed picks between the actual and the lagged morph weight, which staggers parts of a face
    // against each other. The renderer supplies the same value for both weights.
    float speed = positionSpeed.w;
    float weight = mix(weights.x, weights.y, speed);

    if (bCompositeNormals != 0)
    {
        // rgb is the normal delta and a is the wrinkle weight
        vec4 normalWrinkle = (texture(morphAtlas, texCoords.zw) * rangesNormalWrinkle) + offsetsNormalWrinkle;
        outputColor = normalWrinkle * weight;
    }
    else
    {
        outputColor = vec4(positionSpeed.xyz * weight, 0.0);
    }
}
