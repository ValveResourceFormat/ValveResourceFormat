module SceneConstants;

__exported import LightingConstants;

// Handles to the buffers one scene shares across all its draws
public struct SceneBuffers
{
    public DescriptorHandle<ConstantBuffer<LightingConstantsData>> lighting;
};
