namespace ShaderCompilerBench;

/// <summary>
/// Writes the salt into a finished SPIR-V module as a specialization constant, so that the driver
/// is handed something it has not seen before. It goes in after every compiler has had its turn,
/// because a define nobody reads does not survive glslang and an unused constant does not survive
/// spirv-opt, and the driver's disk cache would then serve every iteration after the first. A
/// specialization constant is part of a module's interface, so a driver has to key on it.
/// </summary>
internal static class SpirvSalt
{
    /// <summary>High enough to stay clear of any specialization constant a shader declares itself.</summary>
    private const uint SpecId = 255;

    private const int HeaderWords = 5;
    private const int BoundWord = 3;

    private const ushort OpTypeInt = 21;
    private const ushort OpSpecConstant = 50;
    private const ushort OpDecorate = 71;
    private const uint DecorationSpecId = 1;

    /// <summary>
    /// Every opcode allowed before the types, constants and globals section of a module, which is
    /// where the new constant and its type have to go. The decoration goes right in front of it.
    /// </summary>
    private static readonly HashSet<ushort> Preamble =
    [
        2, 3, 4, 5, 6, 7, 8, 10, 11, 14, 15, 16, 17, 71, 72, 73, 74, 75, 317, 330, 331, 332, 5632, 5633,
    ];

    public static SpirvPair Stamp(SpirvPair spirv, int salt)
        => new(Stamp(spirv.Vertex, salt), Stamp(spirv.Fragment, salt));

    public static byte[] Stamp(byte[] module, int salt)
    {
        var words = new uint[module.Length / sizeof(uint)];
        Buffer.BlockCopy(module, 0, words, 0, module.Length);

        var typesStart = words.Length;
        var typeId = 0u;
        var constantAt = -1;

        for (var i = HeaderWords; i < words.Length;)
        {
            var opcode = (ushort)(words[i] & 0xFFFF);
            var count = (int)(words[i] >> 16);

            if (typesStart == words.Length && !Preamble.Contains(opcode))
            {
                typesStart = i;
            }

            // A 32 bit signed int is the type to reuse, and the constant has to come after it.
            if (opcode == OpTypeInt && words[i + 2] == 32 && words[i + 3] == 1)
            {
                typeId = words[i + 1];
                constantAt = i + count;
                break;
            }

            i += count;
        }

        var constantId = words[BoundWord]++;
        List<uint> declarations = [];

        if (typeId == 0)
        {
            // No such type in the module, so declare one at the top of the section, with the
            // constant right behind it.
            typeId = words[BoundWord]++;
            constantAt = typesStart;
            declarations.AddRange([(4u << 16) | OpTypeInt, typeId, 32, 1]);
        }

        declarations.AddRange([(4u << 16) | OpSpecConstant, typeId, constantId, (uint)salt]);

        List<uint> result = [.. words[..typesStart]];
        result.AddRange([(4u << 16) | OpDecorate, constantId, DecorationSpecId, SpecId]);
        result.AddRange(words[typesStart..constantAt]);
        result.AddRange(declarations);
        result.AddRange(words[constantAt..]);

        var stamped = new byte[result.Count * sizeof(uint)];
        Buffer.BlockCopy(result.ToArray(), 0, stamped, 0, stamped.Length);
        return stamped;
    }
}
