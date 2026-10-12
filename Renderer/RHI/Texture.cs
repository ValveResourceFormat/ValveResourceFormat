using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vma;
using static Vortice.Vulkan.Vulkan;

namespace ValveResourceFormat.Renderer.RHI
{
    public class Texture : Image
    {
        /// <summary>The sampler state a texture is created with when none is given.</summary>
        public static readonly VkSamplerCreateInfo DefaultSamplerInfo = new()
        {
            magFilter = VkFilter.Linear,
            minFilter = VkFilter.Linear,
            mipmapMode = VkSamplerMipmapMode.Linear,
            mipLodBias = 0,
            anisotropyEnable = false,
            minLod = 0,
            maxLod = 1000,
        };

        private VkSamplerCreateInfo samplerInfo;

        public uint BindlessIndex { get; private set; }

        /// <summary>
        /// Gets whether any mip has been uploaded. Until then <see cref="DescriptorHandle"/> is valid to write
        /// but must not be sampled.
        /// </summary>
        public bool IsResident => ResidentMip < MipLevels;

        /// <summary>Gets the most detailed mip shaders may sample, <see cref="Image.MipLevels"/> while nothing is resident.</summary>
        public uint ResidentMip { get; private set; }

        /// <summary>Gets the sampler table slot of this texture's own sampler state, see <see cref="Device.GetSampler"/>.</summary>
        public uint SamplerIndex { get; private set; }

        /// <summary>Gets a bindless handle that samples this texture with its own sampler state.</summary>
        public DescriptorHandle<Texture> DescriptorHandle => new DescriptorHandle<Texture>(BindlessIndex, SamplerIndex);

        /// <summary>Gets a bindless handle that samples this texture with another sampler.</summary>
        /// <remarks>
        /// The sampler's <c>minLod</c> must be at least <see cref="ResidentMip"/>, since only this texture's own
        /// sampler keeps sampling away from mips that have not streamed in yet.
        /// </remarks>
        /// <param name="samplerIndex">The sampler table slot, see <see cref="Device.GetSampler"/>.</param>
        public DescriptorHandle<Texture> GetDescriptorHandleWithSampler(uint samplerIndex) => new DescriptorHandle<Texture>(BindlessIndex, samplerIndex);

        /// <summary>
        /// Creates a sampled texture and takes its bindless slot. It may be sampled once <see cref="SetResidentMip"/> says it has data.
        /// </summary>
        /// <param name="width">Width in texels.</param>
        /// <param name="height">Height in texels, must be 1 for one dimensional textures.</param>
        /// <param name="format">Texel format.</param>
        /// <param name="mipLevels">Number of mip levels.</param>
        /// <param name="dimension">The texture shape, which must match the sampler type the shader declares.</param>
        /// <param name="depthOrArraySize">Depth in texels for 3D textures, the layer count for arrays,
        /// and the number of cubes (not faces) for cube arrays. Must be 1 for everything else.</param>
        /// <param name="samplerInfo">The sampler state, <see cref="DefaultSamplerInfo"/> when not given.</param>
        public Texture(uint width, uint height, VkFormat format = VkFormat.R8G8B8A8Unorm, uint mipLevels = 1,
            TextureDimension dimension = TextureDimension.Texture2D, uint depthOrArraySize = 1, VkSamplerCreateInfo? samplerInfo = null)
            : base(width, height, format, mipLevels: mipLevels, imageUsage: VkImageUsageFlags.Sampled | VkImageUsageFlags.TransferDst,
                dimension: dimension, depthOrArraySize: depthOrArraySize)
        {
            ResidentMip = MipLevels;

            // Clamping minLod on the sampler keeps sampling away from mips that have not arrived, with one view over
            // the whole chain. Samplers are cached by the device, so each texture only adds one per level.
            this.samplerInfo = samplerInfo ?? DefaultSamplerInfo;
            this.samplerInfo.minLod = MipLevels - 1;

            BindlessIndex = RenderDevice!.GetBindlessSlot(VkDescriptorType.CombinedImageSampler, ImageViewHandle, RenderDevice!.CreateSampler(this.samplerInfo));
            SamplerIndex = RenderDevice!.GetSampler(this.samplerInfo);
        }

        /// <summary>
        /// Lets shaders sample <paramref name="mipLevel"/> and every smaller mip. Those mips must already be in
        /// <see cref="VkImageLayout.ShaderReadOnlyOptimal"/> and owned by the graphics queue family for every
        /// frame recorded from here on. Asking for a mip less detailed than what is resident does nothing.
        /// </summary>
        /// <param name="mipLevel">The most detailed mip that has arrived.</param>
        public void SetResidentMip(uint mipLevel)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(mipLevel, MipLevels);

            if (mipLevel >= ResidentMip)
            {
                return;
            }

            ResidentMip = mipLevel;
            samplerInfo.minLod = mipLevel;

            RenderDevice!.UpdateBindlessCombinedSampler(ImageViewHandle, RenderDevice!.CreateSampler(samplerInfo), BindlessIndex);
            SamplerIndex = RenderDevice!.GetSampler(samplerInfo);
        }

        /// <summary>Changes the sampler state this texture is sampled with.</summary>
        /// <param name="info">The new sampler state. Its <c>minLod</c> is replaced to keep sampling on resident mips.</param>
        public void SetSampler(VkSamplerCreateInfo info)
        {
            samplerInfo = info;
            samplerInfo.minLod = Math.Min(ResidentMip, MipLevels - 1);

            RenderDevice!.UpdateBindlessCombinedSampler(ImageViewHandle, RenderDevice!.CreateSampler(samplerInfo), BindlessIndex);
            SamplerIndex = RenderDevice!.GetSampler(samplerInfo);
        }

        /// <summary>Gets a bindless handle to a single mip level, see <see cref="Image.GetMipView"/>.</summary>
        /// <param name="mipLevel">The mip level to reference.</param>
        // VKTODO: Allocate the slot together with the view and free it in Destroy(). Writes need a storage image slot, not a combined sampler.
        public DescriptorHandle<Texture> GetMipDescriptorHandle(uint mipLevel) => throw new NotImplementedException();

        /// <summary>Gets a bindless handle to a single array layer or cube face as a 2D texture, see <see cref="Image.GetLayerView"/>.</summary>
        /// <param name="layer">The array layer to reference, counting each cube face as a layer.</param>
        // VKTODO: Allocate the slot together with the view and free it in Destroy().
        public DescriptorHandle<Texture> GetLayerDescriptorHandle(uint layer) => throw new NotImplementedException();

        // VKTODO: Optimal tiling has no defined texel layout, so mapped memory can't be read or written
        // meaningfully, and AutoPreferDevice usually lands in memory that isn't host visible at all.
        // Upload through a staging buffer and vkCmdCopyBufferToImage instead.
        public unsafe void* Map()
        {
            void* data;
            vmaMapMemory(RenderDevice!.VmaAllocator, VmaAllocation!.Value, &data);
            return data;
        }

        public void Unmap()
        {
            vmaUnmapMemory(RenderDevice!.VmaAllocator, VmaAllocation!.Value);
        }

        /// <inheritdoc/>
        public override void Destroy()
        {
            // Only safe once no frame in flight reads the slot, which is when the destroy queue gets here
            RenderDevice!.FreeBindlessSlot(VkDescriptorType.CombinedImageSampler, BindlessIndex);

            base.Destroy();
        }
    }
}
