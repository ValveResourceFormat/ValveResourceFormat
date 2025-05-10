using System.Diagnostics;

namespace ValveResourceFormat.CompiledShader;

#nullable disable

public class VfxShaderFileVulkan : VfxShaderFile
{
    public override string BlockName => "VULKAN";
    public int Version { get; } = -1;
    public int BytecodeSize { get; } = -1;

    public int Unknown1 { get; }
    public byte[] Unknown2 { get; }

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
    public int[] Unknown17 { get; }
    public short Unknown18 { get; }
    public ushort[] Unknown19 { get; }
    public int Unknown20 { get; }
    public int Unknown21 { get; }
    public int Unknown22 { get; }
    public short Unknown23 { get; }
    public byte[] Unknown24 { get; }
    public short Unknown25 { get; }
    public short Unknown26 { get; }
    public short Unknown27 { get; }
    public short Unknown28 { get; }
    public int[] Unknown29 { get; }
    public uint Unknown30 { get; }
    public int[] Unknown31 { get; }
    public short Unknown32 { get; }
    public byte Unknown33 { get; }
    public byte[] Unknown34 { get; }

    public VfxShaderFileVulkan(ShaderDataReader datareader, int sourceId, bool isMobile) : base(datareader, sourceId)
    {
        // CVfxShaderFile::Unserialize
        if (Size > 0)
        {
            Version = datareader.ReadInt32();

            UnexpectedMagicException.Assert(Version >= 3 && Version <= 5, Version);

            BytecodeSize = datareader.ReadInt32();
            if (BytecodeSize > 0)
            {
                Bytecode = datareader.ReadBytes(BytecodeSize);
            }
        }

        if (Size > 0 && !isMobile)
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
                    Unknown17[i] = datareader.ReadInt32();
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
                Unknown24 = datareader.ReadBytes(Unknown23 * 72);
            }

            Unknown25 = datareader.ReadInt16();
            var a = Unknown25 & 0xFFF;
            var b = Unknown25 >> 12;

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

            if (Version >= 4)
            {
                Unknown30 = datareader.ReadUInt32(); // * 4
                if (Unknown30 > 0)
                {
                    Unknown31 = new int[Unknown30];
                    for (var i = 0; i < Unknown30; i++)
                    {
                        Unknown31[i] = datareader.ReadInt32();
                    }
                }

                // v3 doesnt seem to have this code below, but v4 didnt put it in the if check?
                Unknown32 = datareader.ReadInt16();
                Unknown33 = datareader.ReadByte();

                if (Unknown33 > 0)
                {
                    Unknown34 = datareader.ReadBytes(Unknown33);
                }
            }
        }
        else
        {
            // There's still some alignment or something on mobile, despite having no metadata
            datareader.BaseStream.Position += Size - BytecodeSize - 8;
        }

        var actuallyRead = datareader.BaseStream.Position - Start - 4;
        Debug.Assert(actuallyRead == Size);

        HashMD5 = new Guid(datareader.ReadBytes(16));
    }
}
