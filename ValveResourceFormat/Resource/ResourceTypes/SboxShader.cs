using System;
using System.IO;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.CompiledShader;

namespace ValveResourceFormat.ResourceTypes
{
    public class SboxShader : ResourceData
    {
        public ShaderFile Features { get; private set; }
        public ShaderFile Vertex { get; private set; }
        public ShaderFile Pixel { get; private set; }
        public ShaderFile Geometry { get; private set; }
        public ShaderFile Hull { get; private set; }
        public ShaderFile Domain { get; private set; }
        public ShaderFile Compute { get; private set; }

        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = Offset;

            var featuresOffset = reader.ReadUInt32();
            var featuresSize = reader.ReadUInt32();
            var vertexOffset = reader.ReadUInt32();
            var vertexSize = reader.ReadUInt32();
            var pixelOffset = reader.ReadUInt32();
            var pixelSize = reader.ReadUInt32();
            var geometryOffset = reader.ReadUInt32();
            var geometrySize = reader.ReadUInt32();
            var hullOffset = reader.ReadUInt32();
            var hullSize = reader.ReadUInt32();
            var domainOffset = reader.ReadUInt32();
            var domainSize = reader.ReadUInt32();
            var computeOffset = reader.ReadUInt32();
            var computeSize = reader.ReadUInt32();

            var shaderName = Path.GetFileNameWithoutExtension(resource.FileName);

            if (featuresOffset != 0)
            {
                reader.BaseStream.Position = Offset + featuresOffset;
                Features = new ShaderFile();
                Features.Read(
                    $"{shaderName}_pc_50_features.vcs",
                    new MemoryStream(reader.ReadBytes((int)featuresSize))
                );
            }

            if (vertexOffset != 0)
            {
                reader.BaseStream.Position = Offset + vertexOffset;
                Vertex = new ShaderFile();
                Vertex.Read(
                    $"{shaderName}_pc_50_vs.vcs",
                    new MemoryStream(reader.ReadBytes((int)vertexSize))
                );
            }

            if (pixelOffset != 0)
            {
                reader.BaseStream.Position = Offset + pixelOffset;
                Pixel = new ShaderFile();
                Pixel.Read(
                    $"{shaderName}_pc_50_ps.vcs",
                    new MemoryStream(reader.ReadBytes((int)pixelSize))
                );
            }

            if (geometryOffset != 0)
            {
                reader.BaseStream.Position = Offset + geometryOffset;
                Geometry = new ShaderFile();
                Geometry.Read(
                    $"{shaderName}_pc_50_gs.vcs",
                    new MemoryStream(reader.ReadBytes((int)geometrySize))
                );
            }

            if (hullOffset != 0)
            {
                reader.BaseStream.Position = Offset + hullOffset;
                Hull = new ShaderFile();
                Hull.Read(
                    $"{shaderName}_pc_50_hs.vcs",
                    new MemoryStream(reader.ReadBytes((int)hullSize))
                );
            }

            if (domainOffset != 0)
            {
                reader.BaseStream.Position = Offset + domainOffset;
                Domain = new ShaderFile();
                Domain.Read(
                    $"{shaderName}_pc_50_ds.vcs",
                    new MemoryStream(reader.ReadBytes((int)domainSize))
                );
            }

            if (computeOffset != 0)
            {
                reader.BaseStream.Position = Offset + computeOffset;
                Compute = new ShaderFile();
                Compute.Read(
                    $"{shaderName}_pc_50_cs.vcs",
                    new MemoryStream(reader.ReadBytes((int)computeSize))
                );
            }
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            Features?.PrintSummary((x) => writer.Write(x));
            Vertex?.PrintSummary((x) => writer.Write(x));
            Pixel?.PrintSummary((x) => writer.Write(x));
            Geometry?.PrintSummary((x) => writer.Write(x));
            Hull?.PrintSummary((x) => writer.Write(x));
            Domain?.PrintSummary((x) => writer.Write(x));
            Compute?.PrintSummary((x) => writer.Write(x));
        }
    }
}
