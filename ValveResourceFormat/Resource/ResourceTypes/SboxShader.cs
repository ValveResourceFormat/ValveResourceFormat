using System.Diagnostics;
using System.IO;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.CompiledShader;

namespace ValveResourceFormat.ResourceTypes
{
    public class SboxShader : ResourceData
    {
        public override BlockType Type { get; }
        public ShaderCollection Shaders { get; } = [];

        record struct OnDiskShaderFile(VcsProgramType Type, uint Offset, uint Size);

        public SboxShader()
        {
            // Older files use a DATA block which is equivalent to the newer DXBC
            Type = BlockType.DATA;
        }

        public SboxShader(BlockType platformBlockType)
        {
            Type = platformBlockType;
        }

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
            var platformType = Type switch
            {
                BlockType.DATA or BlockType.DXBC => VcsPlatformType.PC,
                BlockType.SPRV => VcsPlatformType.VULKAN,
                _ => throw new InvalidDataException($"Unable to read {nameof(SboxShader)} constructed with an unknown block type: {Type}"),
            };

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

        public override void WriteText(IndentedTextWriter writer)
        {
            foreach (var shader in Shaders)
            {
                writer.WriteLine(shader.VcsProgramType.ToString());
            }
        }
    }
}
