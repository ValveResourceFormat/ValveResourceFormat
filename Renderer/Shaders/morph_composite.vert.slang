#version 460

in vec4 vPOSITION;
in vec4 vTEXCOORD;
in vec4 vTEXCOORD1;
in vec4 vTEXCOORD2;
in vec4 vTEXCOORD3;
in vec4 vTEXCOORD4;

uniform int bCompositeNormals;
uniform vec2 vAtlasOrigin; // the composite's rect within an atlas field, in texels
uniform vec2 vAtlasFieldSize;

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

    // Positions are texels of the morph set, whose rows are the rect's texel rows. Both fields together span
    // [-1, 1], so the left one is one unit wide and shifting by one lands the normal/wrinkle field beside it.
    vec2 texel = vPOSITION.xy + vAtlasOrigin;
    gl_Position = vec4(texel.x / vAtlasFieldSize.x - 1.0 + float(bCompositeNormals), texel.y / vAtlasFieldSize.y * 2.0 - 1.0, 1.0, 1.0);
}
