#version 460

#pragma IgnoreMaterialData

// Writes full coverage into the single channel outline mask, which outline_post.frag
// then runs edge detection over.
layout (location = 0) out float outOutlineMask;

void main()
{
    outOutlineMask = 1.0;
}
