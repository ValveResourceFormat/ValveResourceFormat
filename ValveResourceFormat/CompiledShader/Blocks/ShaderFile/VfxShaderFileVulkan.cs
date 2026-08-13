using System.Diagnostics;
using System.Globalization;
using System.IO;
using Vortice.SpirvCross;

namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Vulkan SPIR-V shader bytecode.
/// </summary>
public class VfxShaderFileVulkan : VfxShaderFile
{
    /// <inheritdoc/>
    public override string BlockName => "VULKAN";
    /// <summary>Gets the shader file version.</summary>
    public int Version { get; private set; }
    /// <summary>Gets the size of the bytecode.</summary>
    public int BytecodeSize { get; private set; }

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

    public sealed class PerDescriptorSetBindingInfo
    {
        public short NumActiveSamplers { get; init; }
        public short NumActiveUniformBuffers { get; init; }
        public ushort ActiveUniformBindingMask { get; init; }
        public ulong ActiveSamplerBindingMask { get; init; }
        public short NumActiveTextures { get; init; }
        public ulong[] ActiveTextureBindingMask { get; init; } = [];
        public ulong[] ActiveInputAttachmentsBindingMask { get; init; } = [];
        public ushort ActiveImageBindingMask { get; init; }
        public short NumActiveUniformTexelBuffers { get; init; }
        public ulong[] ActiveUniformTexelBufferBindingMask { get; init; } = [];
        // Read out-of-band on the wire (after push constants), so this is the only mutable field.
        public ushort ActiveStorageTexelBufferBindingMask { get; set; }
    }

    public readonly record struct ShaderStorageBufferBinding(ushort BindingAndRegisterSpace, ushort DescriptorSet)
    {
        public int Binding => BindingAndRegisterSpace & 0x3FFF;
        public int RegisterSpace => (BindingAndRegisterSpace >> 14) & 0x3;
    }

    public readonly record struct HiddenUAVCounter(byte AssociatedShaderStorageIndex, byte UAVHiddenCounterBinding);

    /// <summary>
    /// Vertex attribute slot for each vertex shader input location, packed as <c>(usage &lt;&lt; 4) | usageIndex</c>.
    /// The usage is a <see cref="D3DVertexUsage"/>, which together with the index identifies the
    /// <see cref="ResourceTypes.Material.InputSignatureElement"/> bound to that location.
    /// Slots the shader compiler optimized away are still listed, which is why locations are not always contiguous.
    /// </summary>
    public byte[]? AttribMap { get; }
    public PerDescriptorSetBindingInfo? DefaultDescriptorSetBindingInfo { get; }
    public ShaderStorageBufferBinding[]? ShaderStorageBufferBindings { get; }
    public HiddenUAVCounter[]? HiddenUAVCounters { get; }
    public int[]? ThreadGroupSize { get; }
    // Static descriptor set entries are kept as raw bytes because the per-entry layout differs by version
    // (version 4+ = 72 bytes/entry, earlier = 64 bytes/entry).
    public byte[]? StaticDescriptorSetBindingInfoData { get; }
    public int PushConstantSize { get; }
    public bool UseShaderStageName { get; }
    public uint[]? DescriptorSetHashes { get; }
    public uint[]? EntryPoints { get; }
    public short RequiredSubgroupSize { get; }
    /// <summary>
    /// Base scalar type of every vertex shader input location, one byte per entry of <see cref="AttribMap"/>.
    /// The known values are 4 for float and 2 for unsigned int. It is null for stages other than the vertex shader.
    /// </summary>
    public byte[]? VertexInputScalarTypes { get; }
#pragma warning restore CS1591

    /// <summary>
    /// Initializes a new instance with explicit size and hash.
    /// </summary>
    public VfxShaderFileVulkan(BinaryReader datareader, int sourceId, int size, Guid hash, VfxStaticComboData parent)
        : base(sourceId, parent)
    {
        HashMD5 = hash;
        Size = size;

        var end = datareader.BaseStream.Position + size;
        Unserialize(datareader);

        // The bytecode is followed by the same metadata block the binary format has, of which only the attribute map is parsed.
        if (datareader.BaseStream.Position < end)
        {
            AttribMap = ReadAttribMap(datareader);
        }

        datareader.BaseStream.Position = end;
    }

    /// <summary>
    /// Initializes a new instance from pure SPIR-V, unassociated with any combo.
    /// </summary>
    public VfxShaderFileVulkan(byte[] bytecode) : base()
    {
        HashMD5 = Guid.Empty;
        Bytecode = bytecode;
        BytecodeSize = bytecode.Length;
        Size = BytecodeSize;
    }

    /// <summary>
    /// Initializes a new instance from a binary reader.
    /// </summary>
    public VfxShaderFileVulkan(BinaryReader datareader, int sourceId, VfxStaticComboData parent, bool isMobile)
        : base(datareader, sourceId, parent)
    {
        // CVfxShaderFile::Unserialize
        if (Size > 0)
        {
            Unserialize(datareader);

            // Mobile has no metadata block, despite still being padded out to the same size.
            if (!isMobile)
            {
                AttribMap = ReadAttribMap(datareader);
            }
        }

        if (Size > 0 && !isMobile && false)
        {
            var bindingInfo = new PerDescriptorSetBindingInfo
            {
                NumActiveSamplers = datareader.ReadInt16(),
                NumActiveUniformBuffers = datareader.ReadInt16(),
                ActiveUniformBindingMask = datareader.ReadUInt16(),
                ActiveSamplerBindingMask = Version >= 5 ? datareader.ReadUInt32() : datareader.ReadUInt64(),
                NumActiveTextures = datareader.ReadInt16(),
                ActiveTextureBindingMask = [datareader.ReadUInt64(), datareader.ReadUInt64()],
                ActiveInputAttachmentsBindingMask = [datareader.ReadUInt64(), datareader.ReadUInt64()],
                ActiveImageBindingMask = datareader.ReadUInt16(),
                NumActiveUniformTexelBuffers = datareader.ReadInt16(),
                ActiveUniformTexelBufferBindingMask = [datareader.ReadUInt64(), datareader.ReadUInt64()],
            };
            DefaultDescriptorSetBindingInfo = bindingInfo;

            var ssboCount = datareader.ReadInt16();
            if (ssboCount > 0)
            {
                ShaderStorageBufferBindings = new ShaderStorageBufferBinding[ssboCount];
                for (var i = 0; i < ssboCount; i++)
                {
                    if (Version >= 4)
                    {
                        var packed = datareader.ReadUInt16();
                        var descriptorSet = datareader.ReadUInt16();
                        ShaderStorageBufferBindings[i] = new ShaderStorageBufferBinding(packed, descriptorSet);
                    }
                    else
                    {
                        ShaderStorageBufferBindings[i] = new ShaderStorageBufferBinding(datareader.ReadUInt16(), 0);
                    }
                }
            }

            var hiddenUAVCount = datareader.ReadInt16();
            if (hiddenUAVCount > 0)
            {
                HiddenUAVCounters = new HiddenUAVCounter[hiddenUAVCount];
                for (var i = 0; i < hiddenUAVCount; i++)
                {
                    HiddenUAVCounters[i] = new HiddenUAVCounter(datareader.ReadByte(), datareader.ReadByte());
                }
            }

            ThreadGroupSize = [
                datareader.ReadInt32(),
                datareader.ReadInt32(),
                datareader.ReadInt32(),
            ];

            var staticDescriptorSetCount = datareader.ReadInt16();
            if (staticDescriptorSetCount > 0)
            {
                var bytesPerEntry = Version >= 4 ? 72 : 64;
                StaticDescriptorSetBindingInfoData = datareader.ReadBytes(staticDescriptorSetCount * bytesPerEntry);
            }

            var pushConstantBitfield = datareader.ReadInt16();
            PushConstantSize = pushConstantBitfield & 0xFFF;
            UseShaderStageName = ((pushConstantBitfield >> 12) & 1) != 0;

            if (Version >= 4)
            {
                bindingInfo.ActiveStorageTexelBufferBindingMask = datareader.ReadUInt16();

                var descriptorSetHashCount = datareader.ReadInt16();
                if (descriptorSetHashCount > 0)
                {
                    DescriptorSetHashes = new uint[descriptorSetHashCount];
                    for (var i = 0; i < descriptorSetHashCount; i++)
                    {
                        DescriptorSetHashes[i] = datareader.ReadUInt32();
                    }
                }

                var entryPointCount = datareader.ReadUInt32();
                if (entryPointCount > 0)
                {
                    EntryPoints = new uint[entryPointCount];
                    for (var i = 0; i < entryPointCount; i++)
                    {
                        EntryPoints[i] = datareader.ReadUInt32();
                    }
                }

                RequiredSubgroupSize = datareader.ReadInt16();

                var vertexInputScalarTypeCount = datareader.ReadByte();
                if (vertexInputScalarTypeCount > 0)
                {
                    VertexInputScalarTypes = datareader.ReadBytes(vertexInputScalarTypeCount);
                }
            }
        }

        // Skip over the metadata that is not parsed yet.
        var end = Start + 4 + Size;
        Debug.Assert(datareader.BaseStream.Position <= end);
        datareader.BaseStream.Position = end;

        HashMD5 = new Guid(datareader.ReadBytes(16));
    }

    private void Unserialize(BinaryReader datareader)
    {
        Version = datareader.ReadInt32();

        UnexpectedMagicException.Assert(Version >= 2 && Version <= 6, Version);

        BytecodeSize = datareader.ReadInt32();
        if (BytecodeSize > 0)
        {
            Bytecode = datareader.ReadBytes(BytecodeSize);
        }
    }

    private static byte[]? ReadAttribMap(BinaryReader datareader)
    {
        var attribMapSize = datareader.ReadInt32();

        return attribMapSize > 0 ? datareader.ReadBytes(attribMapSize) : null;
    }

    /// <summary>
    /// Gets the Direct3D vertex semantic the vertex layout assigns to a shader input location,
    /// or <see langword="false"/> when this shader has no attribute map or does not use that location.
    /// </summary>
    /// <param name="location">The SPIR-V input location.</param>
    /// <param name="semanticName">The Direct3D semantic name, e.g. <c>TEXCOORD</c>.</param>
    /// <param name="semanticIndex">The Direct3D semantic index.</param>
    public bool TryGetInputSemantic(uint location, out string semanticName, out int semanticIndex)
    {
        if (AttribMap is null || location >= (uint)AttribMap.Length)
        {
            semanticName = string.Empty;
            semanticIndex = 0;
            return false;
        }

        var attribSlot = AttribMap[location];
        var usage = (D3DVertexUsage)(attribSlot >> 4);

        semanticName = usage.ToD3DSemanticName();
        semanticIndex = attribSlot & 0xF;
        return semanticName.Length > 0;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Decompiles SPIR-V bytecode to HLSL or GLSL using SPIRV-Cross reflection, attempting multiple backends until successful.
    /// </remarks>
    public override string GetDecompiledFile()
    {
        using var buffer = new StringWriter(CultureInfo.InvariantCulture);

        var backendsToTry = new[] { Backend.HLSL, Backend.GLSL, /* Backend.MSL, */ };
        for (var i = 0; i < backendsToTry.Length; i++)
        {
            var backend = backendsToTry[i];
            var success = ShaderSpirvReflection.ReflectSpirv(this, backend, out var code);
            if (success)
            {
                buffer.Write(code);
                break;
            }

            buffer.WriteLine($"// SPIR-V reflection failed for backend {backend}:");

            foreach (var line in code.AsSpan().EnumerateLines())
            {
                if (line.Length == 0)
                {
                    continue;
                }

                buffer.Write("// ");
                buffer.WriteLine(line);
            }

            if (i < backendsToTry.Length - 1)
            {
                buffer.WriteLine("//");
                buffer.WriteLine($"// Re-attempting reflection with the {backendsToTry[i + 1]} backend.");
                buffer.WriteLine();
            }
        }

        return buffer.ToString();
    }
}
