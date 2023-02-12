using System.IO;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.CompiledShader;

namespace ValveResourceFormat.ResourceTypes
{
    public class SboxShader : ResourceData
    {
        public ShaderCollection Shaders { get; } = new ShaderCollection();

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
                var features = new ShaderFile();
                features.Read(
                    $"{shaderName}_pc_50_features.vcs",
                    new MemoryStream(reader.ReadBytes((int)featuresSize))
                );
                Shaders.Add(features);
            }

            if (vertexOffset != 0)
            {
                reader.BaseStream.Position = Offset + vertexOffset;
                var vertex = new ShaderFile();
                vertex.Read(
                    $"{shaderName}_pc_50_vs.vcs",
                    new MemoryStream(reader.ReadBytes((int)vertexSize))
                );
                Shaders.Add(vertex);
            }

            if (pixelOffset != 0)
            {
                reader.BaseStream.Position = Offset + pixelOffset;
                var pixel = new ShaderFile();
                pixel.Read(
                    $"{shaderName}_pc_50_ps.vcs",
                    new MemoryStream(reader.ReadBytes((int)pixelSize))
                );
                Shaders.Add(pixel);
            }

            if (geometryOffset != 0)
            {
                reader.BaseStream.Position = Offset + geometryOffset;
                var geometry = new ShaderFile();
                geometry.Read(
                    $"{shaderName}_pc_50_gs.vcs",
                    new MemoryStream(reader.ReadBytes((int)geometrySize))
                );
                Shaders.Add(geometry);
            }

            if (hullOffset != 0)
            {
                reader.BaseStream.Position = Offset + hullOffset;
                var hull = new ShaderFile();
                hull.Read(
                    $"{shaderName}_pc_50_hs.vcs",
                    new MemoryStream(reader.ReadBytes((int)hullSize))
                );
                Shaders.Add(hull);
            }

            if (domainOffset != 0)
            {
                reader.BaseStream.Position = Offset + domainOffset;
                var domain = new ShaderFile();
                domain.Read(
                    $"{shaderName}_pc_50_ds.vcs",
                    new MemoryStream(reader.ReadBytes((int)domainSize))
                );
                Shaders.Add(domain);
            }

            if (computeOffset != 0)
            {
                reader.BaseStream.Position = Offset + computeOffset;
                var compute = new ShaderFile();
                compute.Read(
                    $"{shaderName}_pc_50_cs.vcs",
                    new MemoryStream(reader.ReadBytes((int)computeSize))
                );
                Shaders.Add(compute);
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
