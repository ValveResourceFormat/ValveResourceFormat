#version 460

#ifndef VRF_VERTEX_LAYOUT_INCLUDE
#define VRF_VERTEX_LAYOUT_INCLUDE

// THIS FILE IS THE SINGLE SOURCE OF TRUTH FOR ATTRIBUTE LOCATIONS

// VertexAttributeLocations.cs parses these defines at startup, so the C# VAO code always uses the numbers written here.

// OpenGL guarantees 16 attribute slots; every slot below is taken, so new attributes share a slot with one they can never appear next to (see the shared slots at the bottom).
// Locations are fixed per attribute name so vertex array objects are valid for any (material, depth-only, picking, outline) without per program attribute introspection.

// Hand built geometry (particles, text, grid) does not use this table, each of those pairs with a C# VertexFormat, and its input locations are simply the element
// indices of that format (see VertexFormat.cs).

#define ATTRIBUTE_POSITION 0
#define ATTRIBUTE_BLENDINDICES 1
#define ATTRIBUTE_BLENDWEIGHT 2
#define ATTRIBUTE_TEXCOORD 3
#define ATTRIBUTE_BLENDINDICES2 4
#define ATTRIBUTE_BLENDWEIGHT2 5
#define ATTRIBUTE_NORMAL 6
#define ATTRIBUTE_TANGENT 7
#define ATTRIBUTE_COLOR 8
#define ATTRIBUTE_TEXCOORD1 9
#define ATTRIBUTE_TEXCOORD2 10
#define ATTRIBUTE_TEXCOORD3 11
#define ATTRIBUTE_TEXCOORD4 12
#define ATTRIBUTE_TEXCOORD5 13
#define ATTRIBUTE_LIGHTMAP_UV 14
#define ATTRIBUTE_COLOR1 15

///////// Name aliases / Shared slots /////////

// Sometimes you may want or need to alias the same attribute under another name,
// for example: vPerVertexLighting is the COLOR1 semantic under its engine name.

// Another reason to do this is to get around the 16 attribute limit of opengl by reusing the location of an attribute it can never coexist with in a shader.
// Violating this is a duplicate-location compile error.


// vPerVertexLighting is the COLOR1 semantic under its engine name.
#define ATTRIBUTE_PER_VERTEX_LIGHTING ATTRIBUTE_COLOR1

// Bomb damage phase; those quads are never skinned, so the second skinning stream's slot is free.
#define ATTRIBUTE_PHASE ATTRIBUTE_BLENDINDICES2

// Standalone vFoliageParams only exists when the shader does not also read vTEXCOORD3
#define ATTRIBUTE_FOLIAGE ATTRIBUTE_TEXCOORD3


#endif
