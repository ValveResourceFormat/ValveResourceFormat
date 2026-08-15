#version 460

in vec4 vPOSITION;
in vec4 vTEXCOORD;
in vec4 vTEXCOORD1;
in vec4 vTEXCOORD2;
in vec4 vTEXCOORD3;
in vec4 vTEXCOORD4;

uniform int bCompositeNormals;

out vec4 offsetsPositionSpeed;
out vec4 rangesPositionSpeed;
out vec4 offsetsNormalWrinkle;
out vec4 rangesNormalWrinkle;
out vec2 weights;
out vec4 texCoords;

void main()
{
    offsetsPositionSpeed = vTEXCOORD1;
    rangesPositionSpeed = vTEXCOORD2;
    offsetsNormalWrinkle = vTEXCOORD3;
    rangesNormalWrinkle = vTEXCOORD4;
    weights = vPOSITION.zw;
    texCoords = vTEXCOORD;

    // A rect is placed within the left half of the composite, which is one unit of the [-1, 1]
    // range wide, so shifting by one lands the normal/wrinkle field beside the position/speed one.
    gl_Position = vec4(vPOSITION.x + float(bCompositeNormals), -vPOSITION.y, 1.0, 1.0);
}
