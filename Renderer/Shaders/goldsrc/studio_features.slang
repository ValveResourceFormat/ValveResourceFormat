#version 460

// The texture flags of a studio model, set per material
uniform int F_ALPHA_TEST;
uniform int F_CHROME;
uniform int F_FLAT_SHADE;
// The model adds to the frame buffer
uniform int F_ADDITIVE_BLEND;
// The model is drawn at the color of its texture, without the light of the map
uniform int F_UNLIT;
