#version 460

struct DrawCommand
{
    uint indexCount;
    uint instanceCount;
    uint firstIndex;
    int baseVertex;
    uint baseInstance;
};

struct DrawBounds
{
    vec3 min;
    uint padding1;
    vec3 max;
    uint padding2;
};

struct PackedAABB
{
    uint m_nMin;
    uint m_nMax;
};

struct MeshletCullInfo
{
    PackedAABB m_PackedAABB;
    uint m_PackedCone;
    uint m_nParentDrawBoundsIndex;
};
