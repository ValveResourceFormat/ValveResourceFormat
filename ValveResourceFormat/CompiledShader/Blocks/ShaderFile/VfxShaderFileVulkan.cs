using System.Buffers.Binary;
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
    public override string SourceType => "VULKAN";
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
    /// Maps a specialization constant, identified by the <see cref="StringToken"/> of its variable name, to its SPIR-V constant id.
    /// </summary>
    public readonly record struct SpecializationConstant(uint NameToken, uint ConstantId, uint Unknown);

    /// <summary>
    /// Vertex attribute slot for each vertex shader input location, packed as <c>(usage &lt;&lt; 4) | usageIndex</c>.
    /// The usage is a <see cref="D3DVertexUsage"/>, which together with the index identifies the
    /// <see cref="ResourceTypes.Material.InputSignatureElement"/> bound to that location.
    /// Slots the shader compiler optimized away are still listed, which is why locations are not always contiguous.
    /// </summary>
    public byte[]? AttribMap { get; }
    public PerDescriptorSetBindingInfo? DefaultDescriptorSetBindingInfo { get; private set; }
    public ShaderStorageBufferBinding[]? ShaderStorageBufferBindings { get; private set; }
    public HiddenUAVCounter[]? HiddenUAVCounters { get; private set; }
    public int[]? ThreadGroupSize { get; private set; }
    // Static descriptor set entries are kept as raw bytes because the per-entry layout differs by version
    // (version 4+ = 72 bytes/entry, mobile version 3 = 64 bytes/entry).
    public byte[]? StaticDescriptorSetBindingInfoData { get; private set; }
    public int PushConstantSize { get; private set; }
    public bool UseShaderStageName { get; private set; }
    public uint[]? DescriptorSetHashes { get; private set; }
    public uint[]? EntryPoints { get; private set; }
    public short RequiredSubgroupSize { get; private set; }
    /// <summary>
    /// Base scalar type of every vertex shader input location, one byte per entry of <see cref="AttribMap"/>.
    /// The known values are 4 for float and 2 for unsigned int. It is null for stages other than the vertex shader.
    /// </summary>
    public byte[]? VertexInputScalarTypes { get; private set; }
    public SpecializationConstant[]? SpecializationConstants { get; private set; }
#pragma warning restore CS1591

    /// <summary>
    /// Initializes a new instance with explicit size and hash.
    /// </summary>
    public VfxShaderFileVulkan(BinaryReader datareader, int shaderFileId, int size, Guid hash, VfxStaticComboData parent)
        : base(shaderFileId, parent)
    {
        HashMD5 = hash;
        Size = size;

        var end = datareader.BaseStream.Position + size;
        Unserialize(datareader);

        // The bytecode is followed by the same metadata block the binary format has
        if (datareader.BaseStream.Position < end)
        {
            AttribMap = ReadAttribMap(datareader);

            if (datareader.BaseStream.Position < end && false)
            {
                ReadMetadata(datareader, end);
            }
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
    public VfxShaderFileVulkan(BinaryReader datareader, int shaderFileId, VfxStaticComboData parent)
        : base(datareader, shaderFileId, parent)
    {
        var end = Start + 4 + Size;

        // CVfxShaderFile::Unserialize
        if (Size > 0)
        {
            Unserialize(datareader);
            AttribMap = ReadAttribMap(datareader);
        }

        if (Size > 0 && false)
        {
            ReadMetadata(datareader, end);
        }

        datareader.BaseStream.Position = end;

        HashMD5 = new Guid(datareader.ReadBytes(16));
    }

    /// <summary>
    /// Reads the binding metadata that follows the attribute map. Mobile blobs of version 3 are read like the mobile
    /// engine does, newer versions like the latest desktop engine reader that accepts them. Desktop blobs of version 3
    /// are skipped, because engine builds wrote different layouts under that version with nothing in the file to tell them apart.
    /// Version 5 is skipped, because no engine reader reads it the way it was written.
    /// </summary>
    private void ReadMetadata(BinaryReader datareader, long end)
    {
        var isMobile = ParentCombo.ParentProgramData?.IsMobileVulkan is true;

        if ((Version == 3 && !isMobile) || Version == 5)
        {
            return;
        }

        var data = new byte[end - datareader.BaseStream.Position];
        datareader.BaseStream.ReadExactly(data);
        var reader = new MetadataReader(data);

        if (Version == 2)
        {
            DefaultDescriptorSetBindingInfo = new PerDescriptorSetBindingInfo
            {
                NumActiveSamplers = reader.ReadInt16(),
                NumActiveUniformBuffers = reader.ReadInt16(),
            };
            return;
        }

        var bindingInfo = new PerDescriptorSetBindingInfo
        {
            NumActiveSamplers = reader.ReadInt16(),
            NumActiveUniformBuffers = reader.ReadInt16(),
            ActiveUniformBindingMask = reader.ReadUInt16(),
            ActiveSamplerBindingMask = Version >= 6 ? reader.ReadUInt32() : reader.ReadUInt64(),
            NumActiveTextures = reader.ReadInt16(),
            ActiveTextureBindingMask = [reader.ReadUInt64(), reader.ReadUInt64()],
            ActiveInputAttachmentsBindingMask = [reader.ReadUInt64(), reader.ReadUInt64()],
            ActiveImageBindingMask = reader.ReadUInt16(),
            NumActiveUniformTexelBuffers = reader.ReadInt16(),
            ActiveUniformTexelBufferBindingMask = [reader.ReadUInt64(), reader.ReadUInt64()],
        };
        DefaultDescriptorSetBindingInfo = bindingInfo;

        var ssboCount = reader.ReadUInt16();
        ShaderStorageBufferBindings = new ShaderStorageBufferBinding[ssboCount];
        for (var i = 0; i < ssboCount; i++)
        {
            var descriptorSet = Version >= 4 ? reader.ReadUInt16() : (ushort)0;
            ShaderStorageBufferBindings[i] = new ShaderStorageBufferBinding(reader.ReadUInt16(), descriptorSet);
        }

        var hiddenUAVCount = reader.ReadUInt16();
        HiddenUAVCounters = new HiddenUAVCounter[hiddenUAVCount];
        for (var i = 0; i < hiddenUAVCount; i++)
        {
            HiddenUAVCounters[i] = new HiddenUAVCounter(reader.ReadByte(), reader.ReadByte());
        }

        ThreadGroupSize = [reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()];

        var staticDescriptorSetCount = reader.ReadUInt16();
        StaticDescriptorSetBindingInfoData = reader.ReadBytes(staticDescriptorSetCount * (Version >= 4 ? 72 : 64));

        var pushConstantBitfield = reader.ReadUInt16();

        if (Version == 3)
        {
            PushConstantSize = pushConstantBitfield;
            return;
        }

        PushConstantSize = pushConstantBitfield & 0xFFF;
        UseShaderStageName = ((pushConstantBitfield >> 12) & 1) != 0;
        bindingInfo.ActiveStorageTexelBufferBindingMask = reader.ReadUInt16();

        DescriptorSetHashes = reader.ReadUInt32s(reader.ReadUInt16());
        EntryPoints = reader.ReadUInt32s((int)reader.ReadUInt32());

        RequiredSubgroupSize = reader.ReadInt16();
        VertexInputScalarTypes = reader.ReadBytes(reader.ReadByte());

        if (Version >= 6 && reader.HasRemaining)
        {
            var specializationConstantCount = reader.ReadUInt16();
            SpecializationConstants = new SpecializationConstant[specializationConstantCount];
            for (var i = 0; i < specializationConstantCount; i++)
            {
                SpecializationConstants[i] = new SpecializationConstant(reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32());
            }
        }

        if (Version >= 6 && reader.Overflowed)
        {
            throw new InvalidDataException("Vulkan shader metadata is truncated");
        }
    }

    /// <summary>
    /// Reads little endian values from the metadata, returning zeros past the end like the engine buffer does.
    /// </summary>
    private sealed class MetadataReader(byte[] data)
    {
        private int position;

        public bool HasRemaining => position < data.Length;
        public bool Overflowed { get; private set; }

        private static readonly byte[] Zeros = new byte[8];

        private ReadOnlySpan<byte> Take(int size)
        {
            if (position + size > data.Length)
            {
                Overflowed = true;
                position = data.Length;
                return size <= Zeros.Length ? Zeros.AsSpan(0, size) : new byte[size];
            }

            var span = data.AsSpan(position, size);
            position += size;
            return span;
        }

        public byte ReadByte() => Take(1)[0];
        public short ReadInt16() => BinaryPrimitives.ReadInt16LittleEndian(Take(2));
        public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
        public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
        public byte[] ReadBytes(int size) => Take(size).ToArray();

        public uint[] ReadUInt32s(int count)
        {
            var values = new uint[count];
            for (var i = 0; i < count; i++)
            {
                values[i] = ReadUInt32();
            }

            return values;
        }
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
    /// Gets the Direct3D vertex semantic the vertex layout assigns to a shader input location.
    /// Returns <see langword="false"/> when this shader has no attribute map or does not use that location.
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
    /// When every backend fails, the errors are returned as comments.
    /// </remarks>
    public override string GetDecompiledFile()
    {
        TryGetDecompiledFile(out var code);
        return code;
    }

    /// <summary>
    /// Decompiles SPIR-V bytecode to HLSL or GLSL using SPIRV-Cross reflection, attempting multiple backends until successful.
    /// </summary>
    /// <param name="code">The decompiled code, or the errors of each backend as comments when every backend failed.</param>
    /// <returns><see langword="true"/> if a backend succeeded.</returns>
    public bool TryGetDecompiledFile(out string code)
    {
        using var buffer = new StringWriter(CultureInfo.InvariantCulture);

        var backendsToTry = new[] { Backend.HLSL, Backend.GLSL, /* Backend.MSL, */ };
        for (var i = 0; i < backendsToTry.Length; i++)
        {
            var backend = backendsToTry[i];
            var success = ShaderSpirvReflection.ReflectSpirv(this, backend, out var backendCode);
            if (success)
            {
                buffer.Write(backendCode);
                code = buffer.ToString();
                return true;
            }

            buffer.WriteLine($"// SPIR-V reflection failed for backend {backend}:");

            foreach (var line in backendCode.AsSpan().EnumerateLines())
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

        code = buffer.ToString();
        return false;
    }
}
