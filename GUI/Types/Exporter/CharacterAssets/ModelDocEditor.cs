using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Exporter.CharacterAssets
{
    /// <summary>
    /// A particle a model creates itself, as its "particles_list" game data.
    /// </summary>
    /// <param name="Name">The particle system.</param>
    /// <param name="AttachmentPoint">The attachment it follows, empty to follow the model's origin.</param>
    /// <param name="AttachmentType">How it is attached, e.g. "point_follow".</param>
    /// <param name="Offset">Offset from the attachment.</param>
    sealed record ModelParticle(string Name, string AttachmentPoint, string AttachmentType, Vector3 Offset);

    /// <summary>
    /// Edits decompiled .vmdl files in place, keeping the rest of the text exactly as the model extractor wrote it.
    /// </summary>
    static partial class ModelDocEditor
    {
        /// <summary>
        /// Makes a material group the default one, so the model shows that skin without anything picking it.
        /// </summary>
        /// <param name="vmdl">The .vmdl text.</param>
        /// <param name="skin">Index of the material group, 0 being the default one.</param>
        public static string MakeMaterialGroupDefault(string vmdl, int skin)
        {
            var groups = GetRootChildren(vmdl)
                .FirstOrDefault(static node => node.GetStringProperty("_class") == "MaterialGroupList")?
                .GetArray("children");

            if (groups == null || skin <= 0 || skin >= groups.Count)
            {
                throw new InvalidDataException($"The model has no material group {skin}");
            }

            var remaps = new List<(string Class, string From, string To)>();

            // The skin's remaps win over anything the default group already remapped the same material to
            foreach (var group in new[] { groups[skin], groups[0] })
            {
                foreach (var remap in group.GetArray("remaps") ?? [])
                {
                    var from = remap.GetStringProperty("from");

                    if (!string.IsNullOrEmpty(from) && !remaps.Any(existing => existing.From.Equals(from, StringComparison.OrdinalIgnoreCase)))
                    {
                        remaps.Add((remap.GetStringProperty("_class", "BaseMaterialRemap"), from, remap.GetStringProperty("to", string.Empty)));
                    }
                }
            }

            var text = new StringBuilder("[\n");

            foreach (var (remapClass, from, to) in remaps)
            {
                text.Append(CultureInfo.InvariantCulture, $"\t\t\t\t\t\t\t{{\n\t\t\t\t\t\t\t\t_class = \"{remapClass}\"\n\t\t\t\t\t\t\t\tfrom = \"{from}\"\n\t\t\t\t\t\t\t\tto = \"{to}\"\n\t\t\t\t\t\t\t}},\n");
            }

            text.Append("\t\t\t\t\t\t]");

            var match = DefaultMaterialGroupRemapsRegex().Match(vmdl);

            if (!match.Success)
            {
                throw new InvalidDataException("The model's default material group was not found");
            }

            var remapsArray = match.Groups["remaps"];

            return Validate(string.Concat(vmdl.AsSpan(0, remapsArray.Index), text.ToString(), vmdl.AsSpan(remapsArray.Index + remapsArray.Length)));
        }

        /// <summary>
        /// Adds particles to the model's game data, so the model creates them when it spawns.
        /// </summary>
        public static string AddParticles(string vmdl, IEnumerable<ModelParticle> particles)
        {
            var entries = new StringBuilder();

            foreach (var particle in particles)
            {
                entries.Append(CultureInfo.InvariantCulture, $$"""

                    {
                        _class = "GenericGameData"
                        name = "{{Path.GetFileNameWithoutExtension(particle.Name)}}"
                        game_class = "particle"
                        game_keys =
                        {
                            name = resource:"{{particle.Name}}"
                            attachment_point = "{{particle.AttachmentPoint}}"
                            attachment_type = "{{particle.AttachmentType}}"
                            attachment_offset = [ {{FormatFloat(particle.Offset.X)}}, {{FormatFloat(particle.Offset.Y)}}, {{FormatFloat(particle.Offset.Z)}} ]
                        }
                    },
                    """);
            }

            if (entries.Length == 0)
            {
                return vmdl;
            }

            // Into the model's game data when it has some, or into a new game data list otherwise
            var gameDataList = GameDataListChildrenRegex().Match(vmdl);

            if (gameDataList.Success)
            {
                return Validate(vmdl.Insert(gameDataList.Index + gameDataList.Length, Indent(entries.ToString(), 5)));
            }

            var rootChildren = RootNodeChildrenRegex().Match(vmdl);

            if (!rootChildren.Success)
            {
                throw new InvalidDataException("The model's root node was not found");
            }

            var node = $$"""

                {
                    _class = "GameDataList"
                    children =
                    [{{Indent(entries.ToString(), 2)}}
                    ]
                },
                """;

            return Validate(vmdl.Insert(rootChildren.Index + rootChildren.Length, Indent(node, 3)));
        }

        /// <summary>
        /// Works out how a particle attaches to its model from the control point configuration it plays under in game:
        /// following the attachment control point 0 is driven by, or the model's origin when it has none.
        /// </summary>
        public static ModelParticle ResolveParticle(IFileLoader fileLoader, string particle)
        {
            var attachmentPoint = string.Empty;
            var offset = Vector3.Zero;

            using var resource = fileLoader.LoadFileCompiled(particle);

            if (resource?.DataBlock is ParticleSystem particleSystem)
            {
                var configurations = particleSystem.GetUpgradedData().GetArray("m_controlPointConfigurations") ?? [];

                var configuration = configurations.FirstOrDefault(static configuration =>
                        string.Equals(configuration.GetStringProperty("m_name"), "game", StringComparison.OrdinalIgnoreCase))
                    ?? configurations.FirstOrDefault(static configuration =>
                        !string.Equals(configuration.GetStringProperty("m_name"), "preview", StringComparison.OrdinalIgnoreCase));

                var driver = configuration?.GetArray("m_drivers")?.FirstOrDefault(static driver =>
                    !driver.ContainsKey("m_iControlPoint") || driver.GetInt32Property("m_iControlPoint") == 0);

                if (driver != null)
                {
                    attachmentPoint = driver.GetStringProperty("m_attachmentName", string.Empty);

                    if (driver.ContainsKey("m_vecOffset") && driver.GetFloatArray("m_vecOffset") is [var x, var y, var z])
                    {
                        offset = new Vector3(x, y, z);
                    }
                }
            }

            // Anything else would place the particle once instead of keeping it on the model as it moves
            var attachmentType = attachmentPoint.Length > 0 ? "point_follow" : "absorigin_follow";

            return new ModelParticle(particle, attachmentPoint, attachmentType, offset);
        }

        /// <summary>
        /// Indents text written with four spaces per level the way the model extractor does, with tabs.
        /// </summary>
        private static string Indent(string text, int depth)
        {
            var lines = text.Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                if (line.Length == 0)
                {
                    continue;
                }

                var spaces = line.Length - line.TrimStart(' ').Length;
                lines[i] = new string('\t', depth + spaces / 4) + line.TrimStart(' ');
            }

            return string.Join('\n', lines);
        }

        private static string FormatFloat(float value) => value.ToString("0.0#####", CultureInfo.InvariantCulture);

        private static IReadOnlyList<KVObject> GetRootChildren(string vmdl)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(vmdl));
            KVObject root = KVDocumentExtensions.ParseKV3(stream);

            return root.GetSubCollection("rootNode")?.GetArray("children") ?? [];
        }

        /// <summary>
        /// Makes sure an edit left the file readable, it is better not to edit it at all than to break it.
        /// </summary>
        private static string Validate(string vmdl)
        {
            GetRootChildren(vmdl);
            return vmdl;
        }

        [GeneratedRegex(@"_class\s*=\s*""DefaultMaterialGroup""[^\[\]{}]*?remaps\s*=\s*(?<remaps>\[[^\[\]]*\])", RegexOptions.CultureInvariant)]
        private static partial Regex DefaultMaterialGroupRemapsRegex();

        [GeneratedRegex(@"_class\s*=\s*""GameDataList""\s*children\s*=\s*\[", RegexOptions.CultureInvariant)]
        private static partial Regex GameDataListChildrenRegex();

        [GeneratedRegex(@"_class\s*=\s*""RootNode""\s*children\s*=\s*\[", RegexOptions.CultureInvariant)]
        private static partial Regex RootNodeChildrenRegex();
    }
}
