using System.Text;

namespace ShaderCompilerBench;

/// <summary>
/// Reads the declaration section of a SPIR-V module. When a driver rejects a module and says
/// nothing, what the module asks for and what the driver advertises are the only two lists to
/// compare, so this pulls the first one out of the binary.
/// </summary>
internal static class SpirvInfo
{
    private const int HeaderWords = 5;
    private const int OpExtension = 10;
    private const int OpCapability = 17;

    /// <summary>The first instruction that can only appear after the declarations, used to stop early.</summary>
    private const int OpFunction = 54;

    /// <summary>
    /// The capabilities worth naming. Anything missing is reported by number, which is still enough
    /// to look up in the SPIR-V specification.
    /// </summary>
    private static readonly Dictionary<uint, string> CapabilityNames = new()
    {
        [0] = "Matrix",
        [1] = "Shader",
        [2] = "Geometry",
        [3] = "Tessellation",
        [4] = "Addresses",
        [5] = "Linkage",
        [6] = "Kernel",
        [7] = "Vector16",
        [8] = "Float16Buffer",
        [9] = "Float16",
        [10] = "Float64",
        [11] = "Int64",
        [12] = "Int64Atomics",
        [13] = "ImageBasic",
        [14] = "ImageReadWrite",
        [15] = "ImageMipmap",
        [17] = "Pipes",
        [18] = "Groups",
        [19] = "DeviceEnqueue",
        [20] = "LiteralSampler",
        [21] = "AtomicStorage",
        [22] = "Int16",
        [23] = "TessellationPointSize",
        [24] = "GeometryPointSize",
        [25] = "ImageGatherExtended",
        [27] = "StorageImageMultisample",
        [28] = "UniformBufferArrayDynamicIndexing",
        [29] = "SampledImageArrayDynamicIndexing",
        [30] = "StorageBufferArrayDynamicIndexing",
        [31] = "StorageImageArrayDynamicIndexing",
        [32] = "ClipDistance",
        [33] = "CullDistance",
        [34] = "ImageCubeArray",
        [35] = "SampleRateShading",
        [36] = "ImageRect",
        [37] = "SampledRect",
        [38] = "GenericPointer",
        [39] = "Int8",
        [40] = "InputAttachment",
        [41] = "SparseResidency",
        [42] = "MinLod",
        [43] = "Sampled1D",
        [44] = "Image1D",
        [45] = "SampledCubeArray",
        [46] = "SampledBuffer",
        [47] = "ImageBuffer",
        [48] = "ImageMSArray",
        [49] = "StorageImageExtendedFormats",
        [50] = "ImageQuery",
        [51] = "DerivativeControl",
        [52] = "InterpolationFunction",
        [53] = "TransformFeedback",
        [54] = "GeometryStreams",
        [55] = "StorageImageReadWithoutFormat",
        [56] = "StorageImageWriteWithoutFormat",
        [57] = "MultiViewport",
        [58] = "SubgroupDispatch",
        [59] = "NamedBarrier",
        [60] = "PipeStorage",
        [61] = "GroupNonUniform",
        [62] = "GroupNonUniformVote",
        [63] = "GroupNonUniformArithmetic",
        [64] = "GroupNonUniformBallot",
        [65] = "GroupNonUniformShuffle",
        [66] = "GroupNonUniformShuffleRelative",
        [67] = "GroupNonUniformClustered",
        [68] = "GroupNonUniformQuad",
        [69] = "ShaderLayer",
        [70] = "ShaderViewportIndex",
        [4423] = "SubgroupBallotKHR",
        [4427] = "DrawParameters",
        [4431] = "SubgroupVoteKHR",
        [4433] = "StorageBuffer16BitAccess",
        [4437] = "StoragePushConstant16",
        [5301] = "GroupNonUniformPartitionedNV",
        [5345] = "DemoteToHelperInvocation",
    };

    public sealed record Declarations(List<string> Capabilities, List<string> Extensions);

    public static Declarations Read(byte[] spirv)
    {
        var capabilities = new List<string>();
        var extensions = new List<string>();

        if (spirv.Length < HeaderWords * sizeof(uint))
        {
            return new Declarations(capabilities, extensions);
        }

        var words = new uint[spirv.Length / sizeof(uint)];
        Buffer.BlockCopy(spirv, 0, words, 0, words.Length * sizeof(uint));

        var index = HeaderWords;

        while (index < words.Length)
        {
            var wordCount = (int)(words[index] >> 16);
            var opcode = (int)(words[index] & 0xFFFF);

            if (wordCount == 0 || index + wordCount > words.Length || opcode == OpFunction)
            {
                break;
            }

            switch (opcode)
            {
                case OpCapability when wordCount >= 2:
                    var capability = words[index + 1];
                    capabilities.Add(CapabilityNames.TryGetValue(capability, out var name)
                        ? name
                        : $"capability {capability}");
                    break;

                case OpExtension when wordCount >= 2:
                    extensions.Add(LiteralString(words, index + 1, wordCount - 1));
                    break;
            }

            index += wordCount;
        }

        return new Declarations(capabilities, extensions);
    }

    /// <summary>SPIR-V literal strings are UTF-8 packed four bytes to a word and null terminated.</summary>
    private static string LiteralString(uint[] words, int start, int wordCount)
    {
        var bytes = new byte[wordCount * sizeof(uint)];
        Buffer.BlockCopy(words, start * sizeof(uint), bytes, 0, bytes.Length);

        var end = Array.IndexOf(bytes, (byte)0);
        return Encoding.UTF8.GetString(bytes, 0, end < 0 ? bytes.Length : end);
    }
}
