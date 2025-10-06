using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using Vortice.SpirvCross;

namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Vulkan SPIR-V shader bytecode.
/// </summary>
public class VfxShaderFileVulkan : VfxShaderFile
{
    /// <summary>Gets the platform name.</summary>
    public override string BlockName => "VULKAN";
    /// <summary>Gets the shader file version.</summary>
    public int Version { get; private set; }
    /// <summary>Gets the size of the bytecode.</summary>
    public int BytecodeSize { get; private set; }

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public int Unknown1 { get; }
    public byte[]? Unknown2 { get; }

    public short Unknown3 { get; }
    public short Unknown4 { get; }
    public short Unknown5 { get; }
    public ulong Unknown6 { get; }
    public short Unknown7 { get; }
    public ulong Unknown8 { get; }
    public ulong Unknown9 { get; }
    public ulong Unknown10 { get; }
    public ulong Unknown11 { get; }
    public short Unknown12 { get; }
    public short Unknown13 { get; }
    public ulong Unknown14 { get; }
    public ulong Unknown15 { get; }
    public short Unknown16 { get; }
    public int[]? Unknown17 { get; }
    public short Unknown18 { get; }
    public ushort[]? Unknown19 { get; }
    public int Unknown20 { get; }
    public int Unknown21 { get; }
    public int Unknown22 { get; }
    public short Unknown23 { get; }
    public byte[]? Unknown24 { get; }
    public short Unknown25 { get; }
    public short Unknown26 { get; }
    public short Unknown27 { get; }
    public short Unknown28 { get; }
    public int[]? Unknown29 { get; }
    public uint Unknown30 { get; }
    public int[]? Unknown31 { get; }
    public short Unknown32 { get; }
    public byte Unknown33 { get; }
    public byte[]? Unknown34 { get; }
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
            Unknown1 = datareader.ReadInt32();
            if (Unknown1 > 0)
            {
                Unknown2 = datareader.ReadBytes(Unknown1);
            }

            Unknown3 = datareader.ReadInt16();
            Unknown4 = datareader.ReadInt16();
            Unknown5 = datareader.ReadInt16();

            if (Version >= 5)
            {
                Unknown6 = datareader.ReadUInt32();
            }
            else
            {
                Unknown6 = datareader.ReadUInt64();
            }

            Unknown7 = datareader.ReadInt16();

            // array[2] in v4?
            Unknown8 = datareader.ReadUInt64();
            Unknown9 = datareader.ReadUInt64();

            // array[2] in v4?
            Unknown10 = datareader.ReadUInt64();
            Unknown11 = datareader.ReadUInt64();

            Unknown12 = datareader.ReadInt16();
            Unknown13 = datareader.ReadInt16();

            // array[2] in v4?
            Unknown14 = datareader.ReadUInt64();
            Unknown15 = datareader.ReadUInt64();

            Unknown16 = datareader.ReadInt16(); // * 4
            if (Unknown16 > 0)
            {
                Unknown17 = new int[Unknown16];
                for (var i = 0; i < Unknown16; i++)
                {
                    if (Version >= 4)
                    {
                        Unknown17[i] = datareader.ReadInt32();
                    }
                    else
                    {
                        Unknown17[i] = datareader.ReadInt16();
                    }
                }
            }

            Unknown18 = datareader.ReadInt16(); // * 2
            if (Unknown18 > 0)
            {
                Unknown19 = new ushort[Unknown18];
                for (var i = 0; i < Unknown18; i++)
                {
                    Unknown19[i] = datareader.ReadUInt16();
                }
            }

            Unknown20 = datareader.ReadInt32();
            Unknown21 = datareader.ReadInt32();
            Unknown22 = datareader.ReadInt32();

            Unknown23 = datareader.ReadInt16();
            if (Unknown23 > 0)
            {
                var bytesToRead = Version >= 4 ? 72 : 64;
                Unknown24 = datareader.ReadBytes(Unknown23 * bytesToRead);
            }

            Unknown25 = datareader.ReadInt16();
            var a = Unknown25 & 0xFFF;
            var b = Unknown25 >> 12;

            if (Version >= 4)
            {
                Unknown27 = datareader.ReadInt16();

                Unknown28 = datareader.ReadInt16(); // * 4
                if (Unknown28 > 0)
                {
                    Unknown29 = new int[Unknown28];
                    for (var i = 0; i < Unknown28; i++)
                    {
                        Unknown29[i] = datareader.ReadInt32();
                    }
                }

                Unknown30 = datareader.ReadUInt32(); // * 4
                if (Unknown30 > 0)
                {
                    Unknown31 = new int[Unknown30];
                    for (var i = 0; i < Unknown30; i++)
                    {
                        Unknown31[i] = datareader.ReadInt32();
                    }
                }

                Unknown32 = datareader.ReadInt16();
                Unknown33 = datareader.ReadByte();

                if (Unknown33 > 0)
                {
                    Unknown34 = datareader.ReadBytes(Unknown33);
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

    /// <summary>
    /// Decompiles SPIR-V bytecode to source code.
    /// </summary>
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
                buffer.Write("// ");
                buffer.WriteLine(line);
            }

            if (i < backendsToTry.Length - 1)
            {
                buffer.WriteLine("// ");
                buffer.WriteLine($"// Re-attempting reflection with the {backendsToTry[i + 1]} backend.");
                buffer.WriteLine();
            }
        }

        return buffer.ToString();
    }
}
