using System.IO;
using ValveResourceFormat.CompiledShader;

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Represents an S&amp;box shader resource.
    /// </summary>
    public class SboxShader : Block
    {
        /// <inheritdoc/>
        public override BlockType Type { get; }
        /// <summary>
        /// Gets the shader collection.
        /// </summary>
        public ShaderCollection Shaders { get; } = [];

        record struct OnDiskShaderFile(VcsProgramType Type, uint Offset, uint Size);

        /// <summary>
        /// Initializes a new instance of the <see cref="SboxShader"/> class.
        /// </summary>
        public SboxShader()
        {
            // Older files use a DATA block which is equivalent to the newer DXBC
            Type = BlockType.DATA;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SboxShader"/> class.
        /// </summary>
        public SboxShader(BlockType platformBlockType)
        {
            Type = platformBlockType;
        }

        /// <inheritdoc/>
        public override void Read(BinaryReader reader)
        {
            ArgumentNullException.ThrowIfNull(Resource);

            reader.BaseStream.Position = Offset;

            const int ShaderFileCount = 9;
            Span<OnDiskShaderFile> shaderFiles = stackalloc OnDiskShaderFile[ShaderFileCount];

            for (var i = 0; i < ShaderFileCount; i++)
            {
                shaderFiles[i].Type = (VcsProgramType)i;
                shaderFiles[i].Offset = reader.ReadUInt32();
                shaderFiles[i].Size = reader.ReadUInt32();
            }

            var shaderName = Path.GetFileNameWithoutExtension(Resource.FileName) ?? string.Empty;
            var shaderModelType = VcsShaderModelType._50;

            if (Type != BlockType.SPRV)
            {
                throw new InvalidDataException($"Unable to read {nameof(SboxShader)} constructed with an unknown block type: {Type}");
            }

            var platformType = VcsPlatformType.VULKAN;

            string GetVcsCompatibleFileName(VcsProgramType programType)
            {
                return ShaderUtilHelpers.ComputeVCSFileName(shaderName, programType, platformType, shaderModelType);
            }

            foreach (var onDiskShaderFile in shaderFiles)
            {
                if (onDiskShaderFile.Offset == 0)
                {
                    continue;
                }

                reader.BaseStream.Position = Offset + onDiskShaderFile.Offset;

                var name = GetVcsCompatibleFileName(onDiskShaderFile.Type);
                var stream = new MemoryStream(reader.ReadBytes((int)onDiskShaderFile.Size));

                var shaderFile = new VfxProgramData { IsSbox = true };
                shaderFile.Read(name, stream);
                Shaders.Add(shaderFile);
            }
        }

        /// <inheritdoc/>
        public override void Serialize(Stream stream)
        {
            throw new NotImplementedException("Serializing this block is not yet supported. If you need this, send us a pull request!");
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Lists all shader program types contained in the collection.
        /// </remarks>
        public override void WriteText(IndentedTextWriter writer)
        {
            foreach (var shader in Shaders)
            {
                writer.WriteLine(shader.VcsProgramType.ToString());
            }
        }
    }
}
