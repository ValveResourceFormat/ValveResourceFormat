module Core;

#define BINDLESS_SET_INDEX 1

// Bindings are numbered after their VkDescriptorType, see CreateSharedBindlessDescriptorSetLayout
[vk::binding(0, BINDLESS_SET_INDEX)] __DynamicResource<__DynamicResourceKind.Sampler> gSamplers[];
[vk::binding(2, BINDLESS_SET_INDEX)] __DynamicResource<__DynamicResourceKind.General> gTextures[];
[vk::binding(6, BINDLESS_SET_INDEX)] __DynamicResource<__DynamicResourceKind.General> gUniformBuffers[];
[vk::binding(7, BINDLESS_SET_INDEX)] __DynamicResource<__DynamicResourceKind.General> gStorageBuffers[];

// Combined samplers are split into a texture slot (x) and a sampler slot (y)
#define SPLIT_COMBINED(Combined, Tex)                                   \
    if (T is Combined)                                                  \
    {                                                                   \
        Combined c = __makeCombinedTextureSampler(                      \
            gTextures[h.x].asOpaqueDescriptor<Tex>(),                   \
            gSamplers[h.y].asOpaqueDescriptor<SamplerState>());         \
        return __slang_noop_cast<T>(c);                                 \
    }

export T getDescriptorFromHandle<T>(DescriptorHandle<T> handle) where T : IOpaqueDescriptor
{
    let h = (uint2)handle;
    switch (T.kind)
    {
    case DescriptorKind.Sampler:
        return gSamplers[h.x].asOpaqueDescriptor<T>();
    case DescriptorKind.CombinedTextureSampler:
        SPLIT_COMBINED(Sampler2D, Texture2D)
        SPLIT_COMBINED(Sampler2DArray, Texture2DArray)
        SPLIT_COMBINED(SamplerCube, TextureCube)
        SPLIT_COMBINED(Sampler3D, Texture3D)
        break;
    case DescriptorKind.Texture:
        // Storage images have no table yet
        if (T.descriptorAccess == DescriptorAccess.Read)
            return gTextures[h.x].asOpaqueDescriptor<T>();
        break;
    case DescriptorKind.ConstantBuffer:
        return gUniformBuffers[h.x].asOpaqueDescriptor<T>();
    case DescriptorKind.StorageBuffer:
        return gStorageBuffers[h.x].asOpaqueDescriptor<T>();
    default:
        break;
    }

    // Types without a table of ours land in slang's own heap, which the pipeline layout does not provide
    return defaultGetDescriptorFromHandle(handle, BindlessDescriptorOptions.None);
}

[__AttributeUsage(_AttributeTargets.Var)]
struct ConstantRange
{
    int min;
    int max;
};

[__AttributeUsage(_AttributeTargets.Var)]
struct SrgbRead;

