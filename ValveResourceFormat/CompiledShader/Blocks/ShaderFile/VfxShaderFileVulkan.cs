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

    public byte[]? AttribMap { get; }
    public PerDescriptorSetBindingInfo? DefaultDescriptorSetBindingInfo { get; }
    public ShaderStorageBufferBinding[]? ShaderStorageBufferBindings { get; }
    public HiddenUAVCounter[]? HiddenUAVCounters { get; }
    public int[]? ThreadGroupSize { get; }
    // Static descriptor set entries are kept as raw bytes because the per-entry layout differs by version
    // (v4 = 96 bytes/entry, v6 = 72 bytes/entry).
    public byte[]? StaticDescriptorSetBindingInfoData { get; }
    public int PushConstantSize { get; }
    public bool UseShaderStageName { get; }
    public uint[]? DescriptorSetHashes { get; }
    public uint[]? EntryPoints { get; }
    public short RequiredSubgroupSize { get; }
    public byte[]? UnknownTrailingData { get; }
#pragma warning restore CS1591

    /// <summary>
    /// Initializes a new instance from pre-hashed data.
    /// </summary>
    public VfxShaderFileVulkan(BinaryReader datareader, int sourceId, Guid hash, VfxStaticComboData parent)
        : base(sourceId, parent)
    {
        HashMD5 = hash;
        Unserialize(datareader);
        Size = BytecodeSize + 8;
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
        }

        if (Size > 0 && !isMobile && false)
        {
            var attribMapSize = datareader.ReadInt32();
            if (attribMapSize > 0)
            {
                AttribMap = datareader.ReadBytes(attribMapSize);
            }

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

                var trailingByteCount = datareader.ReadByte();
                if (trailingByteCount > 0)
                {
                    UnknownTrailingData = datareader.ReadBytes(trailingByteCount);
                }
            }
        }
        else if (Size > 0)
        {
            // There's still some alignment or something on mobile, despite having no metadata
            datareader.BaseStream.Position += Size - BytecodeSize - 8;
        }

        var actuallyRead = datareader.BaseStream.Position - Start - 4;
        Debug.Assert(actuallyRead == Size);
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
