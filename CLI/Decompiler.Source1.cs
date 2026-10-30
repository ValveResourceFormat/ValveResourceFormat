using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ValveResourceFormat.Source1.Bsp;
using ValveResourceFormat.Source1.FileSystem;
using ValveResourceFormat.Source1.Material;
using ValveResourceFormat.Source1.Model;
using ValveResourceFormat.Source1.Texture;

namespace CLI
{
    public partial class Decompiler
    {
        private bool PrintBspEntities;

        /// <summary>
        /// Prints Source 1 files, returns false if the file is not one of them.
        /// </summary>
        private bool TryParseSource1File(string path, Stream stream, string? originalPath)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();

            if (extension is not (".bsp" or ".mdl" or ".vtf" or ".vmt"))
            {
                return false;
            }

            if (OutputFile != null)
            {
                ReportError($"\"{path}\" is a Source 1 file, which can only be printed.");
                return true;
            }

            try
            {
                var report = new StringBuilder();
                var statName = string.Empty;

                switch (extension)
                {
                    case ".bsp": statName = DescribeBsp(report, path, stream); break;
                    case ".mdl": statName = DescribeMdl(report, path, stream); break;
                    case ".vtf": statName = DescribeVtf(report, stream); break;
                    case ".vmt": statName = DescribeVmt(report, path, stream); break;
                }

                if (CollectStats)
                {
                    AddNonResourceStat(statName, path, originalPath);
                }
                else
                {
                    Console.Write(report.ToString());
                }
            }
            catch (Exception e)
            {
                LogException(e, path, originalPath);
            }

            return true;
        }

        private string DescribeBsp(StringBuilder report, string path, Stream stream)
        {
            var bsp = BspFile.Open(stream, Path.GetFileNameWithoutExtension(path));

            if (PrintBspEntities)
            {
                var entityLump = bsp.GetLumpData(LumpType.Entities).Span;
                var length = entityLump.IndexOf((byte)0);
                report.AppendLine(Encoding.Latin1.GetString(length < 0 ? entityLump : entityLump[..length]));
                return string.Empty;
            }

            report.AppendLine(CultureInfo.InvariantCulture, $"BSP version {bsp.Version}, map revision {bsp.MapRevision}, {bsp.FileLength:N0} bytes");

            if (bsp.IsL4D2HeaderLayout)
            {
                report.AppendLine("Lump table uses the Left 4 Dead 2 header layout.");
            }

            report.AppendLine();
            report.AppendLine("--- Lumps ---");
            report.AppendLine(CultureInfo.InvariantCulture, $"{"Index",5}  {"Name",-28}{"Offset",10}{"Length",10}{"Version",9}  Compression");

            foreach (var lump in bsp.Lumps)
            {
                var compression = string.Empty;

                if (lump.Length > 0)
                {
                    var raw = bsp.GetFileData(lump.Offset, lump.Length).Span;

                    if (BspFile.IsLzmaCompressed(raw))
                    {
                        compression = string.Create(CultureInfo.InvariantCulture, $"LZMA, {bsp.GetLumpData(lump.Type).Length:N0} bytes unpacked");
                    }
                }

                report.AppendLine(CultureInfo.InvariantCulture, $"{(int)lump.Type,5}  {lump.Type,-28}{lump.Offset,10}{lump.Length,10}{lump.Version,9}  {compression}");
            }

            report.AppendLine();
            report.AppendLine("--- Entities ---");

            var entities = bsp.Entities;
            report.AppendLine(CultureInfo.InvariantCulture, $"Count: {entities.Count}");

            var classNames = entities
                .GroupBy(entity => entity.ClassName, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var group in classNames)
            {
                var className = group.Key.Length == 0 ? "(no classname)" : group.Key;
                report.AppendLine(CultureInfo.InvariantCulture, $"  {group.Count(),6}  {className}");
            }

            report.AppendLine();
            report.AppendLine("--- Geometry ---");
            report.AppendLine(CultureInfo.InvariantCulture, $"Faces: {bsp.Faces.Length}");
            report.AppendLine(CultureInfo.InvariantCulture, $"HDR faces: {bsp.FacesHdr.Length}");
            report.AppendLine(CultureInfo.InvariantCulture, $"Models: {bsp.Models.Length}");
            report.AppendLine(CultureInfo.InvariantCulture, $"Displacements: {bsp.DisplacementInfos.Length}");
            report.AppendLine(CultureInfo.InvariantCulture, $"Overlays: {bsp.Overlays.Count}");
            report.AppendLine(CultureInfo.InvariantCulture, $"Cubemaps: {bsp.Cubemaps.Length}");
            report.AppendLine(CultureInfo.InvariantCulture, $"HDR lighting: {(bsp.HasHdrLighting ? "yes" : "no")}");

            report.AppendLine();
            report.AppendLine("--- Props ---");

            var staticProps = bsp.StaticProps;

            if (staticProps.Props.Count == 0 && staticProps.ModelDictionary.Count == 0)
            {
                report.AppendLine("Static props: none");
            }
            else
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"Static props: {staticProps.Props.Count} (lump version {staticProps.LumpVersion}, entry size {staticProps.EntrySize}, {staticProps.ModelDictionary.Count} models used)");
            }

            var detailProps = bsp.DetailProps;
            report.AppendLine(CultureInfo.InvariantCulture, $"Detail props: {detailProps.Props.Count} (lump version {detailProps.LumpVersion}, {detailProps.ModelDictionary.Count} models, {detailProps.SpriteDictionary.Count} sprites)");

            report.AppendLine();
            report.AppendLine("--- Pakfile ---");

            using var pakfile = BspPakFileReader.Open(bsp);

            if (pakfile == null)
            {
                report.AppendLine("Files: none");
            }
            else
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"Files: {pakfile.Entries.Count}");
            }

            return $"BSP version {bsp.Version}";
        }

        private static string DescribeMdl(StringBuilder report, string path, Stream stream)
        {
            using var memory = new MemoryStream();
            stream.CopyTo(memory);

            var mdl = new MdlFile(memory.ToArray());

            using var fileSystem = Source1FileSystem.FromPath(path, "models") ?? Source1FileSystem.FromDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var mdlPath = fileSystem.GetGamePath(path) ?? Path.GetFileName(path);
            var model = Source1Model.Load(mdlPath, fileSystem.OpenFile, requireGeometry: false);

            var sequences = model.Sequences;
            var flags = (MdlFile.StudioFlags)mdl.Flags;
            var hasGeometry = model.NumLods > 0;

            report.AppendLine(CultureInfo.InvariantCulture, $"MDL version {mdl.Version}, name \"{mdl.Name}\"");
            report.AppendLine(CultureInfo.InvariantCulture, $"Checksum: 0x{(uint)mdl.Checksum:X8}");
            report.AppendLine(CultureInfo.InvariantCulture, $"Flags: 0x{mdl.Flags:X} ({flags})");
            report.AppendLine(CultureInfo.InvariantCulture, $"Surface property: {mdl.SurfaceProperty}");
            report.AppendLine(CultureInfo.InvariantCulture, $"Mass: {model.Mass:0.###}");
            report.AppendLine(CultureInfo.InvariantCulture, $"Eye position: {model.EyePosition}");
            report.AppendLine(CultureInfo.InvariantCulture, $"Hull: {model.HullMin} to {model.HullMax}");
            report.AppendLine(CultureInfo.InvariantCulture, $"View bounds: {model.ViewMin} to {model.ViewMax}");
            report.AppendLine(CultureInfo.InvariantCulture, $"Geometry: {(hasGeometry ? "loaded from VVD and VTX" : "not loaded, VVD or VTX is missing or the model has no body parts")}");
            report.AppendLine(CultureInfo.InvariantCulture, $"Physics: {model.PhySolids.Length} solids");

            foreach (var warning in model.Warnings)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"Warning: {warning}");
            }

            report.AppendLine();
            report.AppendLine(CultureInfo.InvariantCulture, $"--- Bones: {mdl.Bones.Length} ---");

            for (var i = 0; i < mdl.Bones.Length; i++)
            {
                var bone = mdl.Bones[i];
                report.AppendLine(CultureInfo.InvariantCulture, $"  [{i}] {mdl.BoneNames[i]}, parent {bone.Parent}, flags 0x{bone.Flags:X}, physics bone {bone.PhysicsBone}");
            }

            report.AppendLine();
            report.AppendLine(CultureInfo.InvariantCulture, $"--- Body parts: {mdl.BodyParts.Length} ---");

            for (var partIndex = 0; partIndex < mdl.BodyParts.Length; partIndex++)
            {
                var models = mdl.Models[partIndex];
                report.AppendLine(CultureInfo.InvariantCulture, $"  [{partIndex}] {mdl.BodyPartNames[partIndex]}, base {mdl.BodyParts[partIndex].Base}, {models.Length} models");

                for (var modelIndex = 0; modelIndex < models.Length; modelIndex++)
                {
                    var meshes = mdl.Meshes[partIndex][modelIndex];
                    report.AppendLine(CultureInfo.InvariantCulture, $"    [{modelIndex}] \"{models[modelIndex].Name}\", {models[modelIndex].NumVertices} vertices, {meshes.Length} meshes");

                    for (var meshIndex = 0; meshIndex < meshes.Length; meshIndex++)
                    {
                        var mesh = meshes[meshIndex];
                        var material = GetSkinMaterialName(mdl, mesh.Material);
                        report.AppendLine(CultureInfo.InvariantCulture, $"      [{meshIndex}] skin ref {mesh.Material} ({material}), {mesh.NumVertices} vertices");
                    }
                }
            }

            report.AppendLine();

            if (hasGeometry)
            {
                var switchPoints = string.Join(", ", model.LodSwitchPoints.Select(point => point.ToString("0.###", CultureInfo.InvariantCulture)));
                report.AppendLine(CultureInfo.InvariantCulture, $"--- LODs: {model.NumLods} (switch points {switchPoints}) ---");
            }
            else
            {
                report.AppendLine("--- LODs: unknown without VTX ---");
            }

            report.AppendLine();
            report.AppendLine(CultureInfo.InvariantCulture, $"--- Material search directories: {mdl.CdMaterials.Length} ---");

            foreach (var directory in mdl.CdMaterials)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"  {directory}");
            }

            report.AppendLine();
            report.AppendLine(CultureInfo.InvariantCulture, $"--- Materials: {mdl.MaterialNames.Length} ---");

            for (var i = 0; i < mdl.MaterialNames.Length; i++)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"  [{i}] {mdl.MaterialNames[i]}");
            }

            report.AppendLine();
            report.AppendLine(CultureInfo.InvariantCulture, $"--- Skin families: {mdl.SkinFamilies.Length}, {mdl.Hdr.NumSkinRef} references each ---");

            for (var i = 0; i < mdl.SkinFamilies.Length; i++)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"  [{i}] {string.Join(", ", mdl.SkinFamilies[i])}");
            }

            report.AppendLine();
            report.AppendLine(CultureInfo.InvariantCulture, $"--- Include models: {mdl.IncludeModels.Length} ---");

            foreach (var include in mdl.IncludeModels)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"  {include}");
            }

            report.AppendLine();
            report.AppendLine(CultureInfo.InvariantCulture, $"--- Pose parameters: {model.PoseParameters.Length} ---");

            foreach (var parameter in model.PoseParameters)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"  {parameter.Name}: {parameter.Start:0.###} to {parameter.End:0.###}, loop {parameter.Loop:0.###}");
            }

            report.AppendLine();
            report.AppendLine(CultureInfo.InvariantCulture, $"--- Sequences: {sequences.Length}, including the ones of included models ---");

            for (var i = 0; i < sequences.Length; i++)
            {
                var sequence = sequences[i];
                var activity = sequence.ActivityName.Length == 0 ? "none" : $"{sequence.ActivityName} (weight {sequence.ActivityWeight})";
                report.AppendLine(CultureInfo.InvariantCulture, $"  [{i}] {sequence.Name}, {sequence.Fps:0.###} fps, {sequence.FrameCount} frames, flags 0x{(int)sequence.Flags:X} ({sequence.Flags}), activity {activity}");
            }

            report.AppendLine();
            report.AppendLine(CultureInfo.InvariantCulture, $"--- Attachments: {mdl.Attachments.Length} ---");

            for (var i = 0; i < mdl.Attachments.Length; i++)
            {
                var attachment = mdl.Attachments[i];
                var boneName = GetBoneName(mdl, attachment.LocalBone);
                report.AppendLine(CultureInfo.InvariantCulture, $"  [{i}] {mdl.AttachmentNames[i]}, bone {attachment.LocalBone} ({boneName}), flags 0x{attachment.Flags:X}");
            }

            report.AppendLine();
            report.AppendLine(CultureInfo.InvariantCulture, $"--- Hitbox sets: {mdl.HitboxSets.Length} ---");

            foreach (var set in mdl.HitboxSets)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"  \"{set.Name}\", {set.Hitboxes.Length} hitboxes");

                for (var i = 0; i < set.Hitboxes.Length; i++)
                {
                    var hitbox = set.Hitboxes[i];
                    var boneName = GetBoneName(mdl, hitbox.Bone);
                    report.AppendLine(CultureInfo.InvariantCulture, $"    [{i}] group {hitbox.Group}, bone {hitbox.Bone} ({boneName}), {hitbox.Min} to {hitbox.Max}");
                }
            }

            return $"MDL version {mdl.Version}";
        }

        private static string GetBoneName(MdlFile mdl, int bone)
        {
            return bone >= 0 && bone < mdl.BoneNames.Length ? mdl.BoneNames[bone] : "?";
        }

        private static string GetSkinMaterialName(MdlFile mdl, int skinReference)
        {
            if (mdl.SkinFamilies.Length == 0 || skinReference < 0 || skinReference >= mdl.SkinFamilies[0].Length)
            {
                return "?";
            }

            var materialIndex = mdl.SkinFamilies[0][skinReference];

            return materialIndex >= 0 && materialIndex < mdl.MaterialNames.Length ? mdl.MaterialNames[materialIndex] : "?";
        }

        private static string DescribeVtf(StringBuilder report, Stream stream)
        {
            var vtf = new VtfFile(stream, onlyHeader: true);
            var header = vtf.Header;

            report.AppendLine(CultureInfo.InvariantCulture, $"VTF version {header.MajorVersion}.{header.MinorVersion}, header size {header.HeaderSize}");
            report.AppendLine(CultureInfo.InvariantCulture, $"Size: {vtf.Width}x{vtf.Height}");
            report.AppendLine(CultureInfo.InvariantCulture, $"Format: {vtf.Format}");
            report.AppendLine(CultureInfo.InvariantCulture, $"Flags: 0x{(uint)vtf.Flags:X8} ({vtf.Flags})");
            report.AppendLine(CultureInfo.InvariantCulture, $"Mipmaps: {vtf.MipmapCount}");
            report.AppendLine(CultureInfo.InvariantCulture, $"Frames: {vtf.FrameCount}, first frame {header.FirstFrame}");
            report.AppendLine(CultureInfo.InvariantCulture, $"Faces: {vtf.FaceCount}");
            report.AppendLine(CultureInfo.InvariantCulture, $"Depth: {vtf.Depth}");
            report.AppendLine(CultureInfo.InvariantCulture, $"Reflectivity: {vtf.Reflectivity}");
            report.AppendLine(CultureInfo.InvariantCulture, $"Bump scale: {header.BumpmapScale:0.###}");
            report.AppendLine(CultureInfo.InvariantCulture, $"Low resolution image: {header.LowResWidth}x{header.LowResHeight} {header.LowResFormat}");

            if (vtf.Resources.Count > 0)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"Resources: {vtf.Resources.Count}");

                foreach (var resource in vtf.Resources)
                {
                    var meaning = resource.HasDataChunk ? "offset" : "value";
                    report.AppendLine(CultureInfo.InvariantCulture, $"  {GetVtfResourceName(resource)}, flags 0x{resource.Flags:X}, {meaning} {resource.Data}");
                }
            }

            return $"VTF version {header.MajorVersion}.{header.MinorVersion}";
        }

        private static string GetVtfResourceName(VtfResource resource)
        {
            switch (resource.Tag)
            {
                case 0x01: return "low resolution image";
                case 0x10: return "sheet";
                case 0x30: return "high resolution image";
            }

            Span<char> tag = stackalloc char[3];

            for (var i = 0; i < tag.Length; i++)
            {
                var value = (byte)(resource.Tag >> (i * 8));
                tag[i] = value is >= 0x20 and < 0x7F ? (char)value : '?';
            }

            return new string(tag);
        }

        private static string DescribeVmt(StringBuilder report, string path, Stream stream)
        {
            using var fileSystem = Source1FileSystem.FromPath(path, "materials")
                ?? throw new InvalidOperationException("The material is not inside of a game folder, there is no gameinfo.txt or materials folder above it.");

            var vmtPath = fileSystem.GetGamePath(path)
                ?? throw new InvalidOperationException("The material is outside of the game folder.");

            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var data = memory.ToArray();

            Stream? OpenFile(string requestedPath)
            {
                return requestedPath.Equals(vmtPath, StringComparison.OrdinalIgnoreCase)
                    ? new MemoryStream(data)
                    : fileSystem.OpenFile(requestedPath);
            }

            var vmt = VmtFile.Load(vmtPath, OpenFile);

            report.AppendLine(CultureInfo.InvariantCulture, $"Material {vmtPath}");
            report.AppendLine(CultureInfo.InvariantCulture, $"Shader: {vmt.ShaderName} (normalized {vmt.NormalizedShaderName})");
            report.AppendLine(CultureInfo.InvariantCulture, $"Roots: {string.Join(", ", vmt.Roots.Select(root => root.Name))}");

            report.AppendLine();
            report.AppendLine(CultureInfo.InvariantCulture, $"--- Parameters after patch, include and fallback resolution: {vmt.Parameters.Count} ---");

            foreach (var (key, value) in vmt.Parameters.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"  {key} = {value}");
            }

            if (vmt.Diagnostics.Count > 0)
            {
                report.AppendLine();
                report.AppendLine(CultureInfo.InvariantCulture, $"--- Diagnostics: {vmt.Diagnostics.Count} ---");

                foreach (var diagnostic in vmt.Diagnostics)
                {
                    report.AppendLine(CultureInfo.InvariantCulture, $"  {diagnostic.Path}:{diagnostic.Line} {diagnostic.Kind}: {diagnostic.Message}");
                }
            }

            var baseTextureHasAlpha = BaseTextureHasAlpha(fileSystem, vmt);
            var material = Source1MaterialConverter.ToVrfMaterial(vmt, vmtPath, baseTextureHasAlpha);

            report.AppendLine();
            report.AppendLine("--- Converted Source 2 material ---");
            report.AppendLine(CultureInfo.InvariantCulture, $"Shader: {material.ShaderName}");
            report.AppendLine(CultureInfo.InvariantCulture, $"Base texture has alpha: {(baseTextureHasAlpha ? "yes" : "no")}");

            report.AppendLine("Combos:");

            foreach (var (name, value) in material.IntParams.Where(pair => pair.Key.StartsWith("F_", StringComparison.Ordinal)).OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"  {name} = {value}");
            }

            report.AppendLine("Textures:");

            foreach (var (name, value) in material.TextureParams.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var status = fileSystem.Exists(value) ? string.Empty : " (missing)";
                report.AppendLine(CultureInfo.InvariantCulture, $"  {name} = {value}{status}");
            }

            report.AppendLine("Other parameters:");

            foreach (var (name, value) in material.IntParams.Where(pair => !pair.Key.StartsWith("F_", StringComparison.Ordinal)).OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"  {name} = {value}");
            }

            foreach (var (name, value) in material.FloatParams.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"  {name} = {value}");
            }

            foreach (var (name, value) in material.VectorParams.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"  {name} = {value}");
            }

            return $"VMT shader {vmt.NormalizedShaderName}";
        }

        private static bool BaseTextureHasAlpha(Source1FileSystem fileSystem, VmtFile vmt)
        {
            var baseTexture = vmt.Values.String("$basetexture");

            if (string.IsNullOrWhiteSpace(baseTexture))
            {
                return true;
            }

            using var stream = fileSystem.OpenFile(VmtFile.NormalizePath(baseTexture, ".vtf"));

            if (stream == null)
            {
                return true;
            }

            try
            {
                var header = new VtfFile(stream, onlyHeader: true).Header;
                return (header.Flags & (VtfFlags.ONEBITALPHA | VtfFlags.EIGHTBITALPHA)) != 0;
            }
            catch (Exception e) when (e is InvalidDataException or IOException or NotSupportedException)
            {
                return true;
            }
        }
    }
}
