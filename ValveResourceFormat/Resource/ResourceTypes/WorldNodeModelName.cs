using System.Globalization;
using System.IO;

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// The parsed name of a world node, resourcecompiler encodes flags into it.
    /// </summary>
    public sealed record WorldNodeModelName
    {
        /// <summary>The name of a model that is not a world node model.</summary>
        public static WorldNodeModelName Unknown { get; } = new();

        /// <summary>Gets the type the compiler assigned to this worldnode.</summary>
        public WorldNodeTypes Type { get; init; }

        /// <summary>Gets the settings the name encodes that have no object type flag.</summary>
        public WorldNodeExtraFlags Flags { get; init; }

        /// <summary>Gets the world node index, -1 when unknown.</summary>
        public int Node { get; init; } = -1;

        /// <summary>Gets the index into <see cref="WorldNode.LayerNames"/>, -1 when unknown.</summary>
        public int Layer { get; init; } = -1;

        /// <summary>Gets the render cluster index, -1 for classes that are not split into clusters.</summary>
        public int Cluster { get; init; } = -1;

        /// <summary>
        /// Gets the counter the compiler numbers models of a class with, such as the 3 of <c>nomerge3</c> or the
        /// trailing number of an aggregate, -1 when the name has none.
        /// </summary>
        public int Index { get; init; } = -1;

        /// <summary>
        /// Gets the file base of the first mesh's material, truncated for some classes, or the clutter model's file
        /// base or the skybox slot. Empty when the name has none.
        /// </summary>
        public string SourceName { get; init; } = string.Empty;

        /// <summary>Gets the object type flags the name encodes; the scene object may set more.</summary>
        public ObjectTypeFlags ObjectFlags { get; init; }

        /// <summary>Gets the cubemap handshake of the <c>_cm</c> suffix, 0 when the name has none.</summary>
        public int CubeMapHandshake { get; init; }

        /// <summary>Gets the light probe volume handshake of the <c>_lp</c> suffix, 0 when the name has none.</summary>
        public int LightProbeHandshake { get; init; }

        [Flags]
        private enum ClassParts
        {
            None = 0,
            Index = 0x1,
            Shadow = 0x2, // _shadow or _noshadow after the index
            Source = 0x4,
            OptionalSource = 0x8,
            Suffixes = 0x10, // group suffixes at the end of the name
        }

        private readonly record struct ClassName(string Prefix, WorldNodeTypes Type, ClassParts Parts, string SourceSeparator = "_");

        // Matched by prefix in order, so a name comes before the shorter names it starts with
        private static readonly ClassName[] ClassNames =
        [
            new("mesh_blocklight_skybox", WorldNodeTypes.SkyboxBlockLight, ClassParts.Index | ClassParts.Shadow | ClassParts.Suffixes),
            new("mesh_blocklight", WorldNodeTypes.BlockLight, ClassParts.Index | ClassParts.Shadow | ClassParts.Suffixes),
            new("mesh_skyboxslot", WorldNodeTypes.SkyboxMaterial, ClassParts.Source | ClassParts.Suffixes),
            new("mesh_skybox", WorldNodeTypes.Skybox, ClassParts.Suffixes),
            new("mesh_nsoverlay", WorldNodeTypes.NoSplitOverlay, ClassParts.Index | ClassParts.Suffixes),
            new("mesh_overlay", WorldNodeTypes.Overlay, ClassParts.Index | ClassParts.Suffixes),
            new("mesh_worldspace_texture_blend", WorldNodeTypes.WorldSpaceTexture, ClassParts.Suffixes),
            new("mesh_prop", WorldNodeTypes.BakedPropLod, ClassParts.Index | ClassParts.Source),
            new("nosplit", WorldNodeTypes.NoSplit, ClassParts.Index | ClassParts.Source),
            new("nomerge", WorldNodeTypes.NoMerge, ClassParts.Index | ClassParts.OptionalSource),
            new("vism", WorldNodeTypes.Aggregate, ClassParts.Index | ClassParts.Source, "_mt_"),
            new("mesh_base", WorldNodeTypes.Base, ClassParts.Suffixes),
            new("mesh_mat", WorldNodeTypes.Base, ClassParts.Index | ClassParts.Source | ClassParts.Suffixes),
            new("mesh", WorldNodeTypes.Aggregate, ClassParts.Suffixes),
        ];

        // Tags between s or d and the class name; bl before b so it is not read as b
        private static readonly (string Tag, ObjectTypeFlags ObjectFlag, WorldNodeExtraFlags Flag)[] Tags =
        [
            ("_cb", ObjectTypeFlags.RenderToCubemaps, WorldNodeExtraFlags.None),
            ("_dl", ObjectTypeFlags.DisabledInLowQuality, WorldNodeExtraFlags.None),
            ("_bl", ObjectTypeFlags.BlockLight, WorldNodeExtraFlags.None),
            ("_b", ObjectTypeFlags.BakedGeometry, WorldNodeExtraFlags.None),
            ("_kv", ObjectTypeFlags.None, WorldNodeExtraFlags.KeepVertices),
            ("_nv", ObjectTypeFlags.DisableVisCulling, WorldNodeExtraFlags.None),
            ("_nds", ObjectTypeFlags.NeedsDynamicShadows, WorldNodeExtraFlags.None),
        ];

        private static readonly (string Kind, WorldNodeTypes Type)[] AggregateKinds =
        [
            ("merge", WorldNodeTypes.Aggregate),
            ("nomerge", WorldNodeTypes.NoMerge),
            ("inst", WorldNodeTypes.NoSplit),
            ("prop", WorldNodeTypes.InstancedProp),
            ("model", WorldNodeTypes.BakedPropLod),
            ("overlay", WorldNodeTypes.Overlay),
        ];

        /// <summary>
        /// Reads what the compiler encoded in the name of a world node model.
        /// </summary>
        /// <param name="modelName">The model path, as in <c>m_renderableModel</c>.</param>
        /// <returns>The parsed name, or <see cref="Unknown"/> when the model is not in a <c>worldnodes</c> folder or its name follows no known pattern.</returns>
        public static WorldNodeModelName Parse(string? modelName)
        {
            if (string.IsNullOrEmpty(modelName))
            {
                return Unknown;
            }

            var directory = Path.GetFileName(Path.GetDirectoryName(modelName.AsSpan()));
            if (!directory.Equals("worldnodes", StringComparison.OrdinalIgnoreCase))
            {
                return Unknown;
            }

            var name = Path.GetFileNameWithoutExtension(modelName.AsSpan());

            // n{node}, or node{node} from older compilers
            if (!(Skip(ref name, "node") || Skip(ref name, "n")) || !ReadNumber(ref name, out var node))
            {
                return Unknown;
            }

            if (!Skip(ref name, "_lr"))
            {
                return ParseLegacyAggregate(name, node);
            }

            if (!ReadNumber(ref name, out var layer))
            {
                return Unknown;
            }

            if (Skip(ref name, "_agg_"))
            {
                return ParseAggregate(name, node, layer);
            }

            if (Skip(ref name, "_clutter"))
            {
                return ReadNumber(ref name, out var index) && Skip(ref name, "_") && !name.IsEmpty
                    ? new() { Type = WorldNodeTypes.Clutter, Node = node, Layer = layer, Index = index, SourceName = name.ToString() }
                    : Unknown;
            }

            return ParseSceneObject(name, node, layer);
        }

        // {node}_lr{layer}[_c{cluster}]_{s|d}[tags]_{class}[suffixes]; older compilers leave out the underscores around s and before the class
        private static WorldNodeModelName ParseSceneObject(ReadOnlySpan<char> name, int node, int layer)
        {
            var cluster = -1;
            if (Skip(ref name, "_c") && !ReadNumber(ref name, out cluster))
            {
                return Unknown;
            }

            Skip(ref name, "_");

            var objectFlags = ObjectTypeFlags.None;
            var flags = WorldNodeExtraFlags.None;

            if (Skip(ref name, "d"))
            {
                objectFlags |= ObjectTypeFlags.RenderWithDynamic;
            }
            else if (!Skip(ref name, "s"))
            {
                return Unknown;
            }

            while (ReadTag(ref name, ref objectFlags, ref flags))
            {
            }

            Skip(ref name, "_");

            if (FindClassName(name) is not { } className)
            {
                return Unknown;
            }

            name = name[className.Prefix.Length..];

            var cubeMapHandshake = 0;
            var lightProbeHandshake = 0;

            // Group suffixes, read from the end since material names have underscores too; older compilers also put _b and _nv here
            if (className.Parts.HasFlag(ClassParts.Suffixes))
            {
                if (SkipEnd(ref name, "_nv"))
                {
                    objectFlags |= ObjectTypeFlags.DisableVisCulling;
                }

                if (SkipEnd(ref name, "_fw"))
                {
                    flags |= WorldNodeExtraFlags.ForwardLayerOnly;
                }

                // The no shadows bit of the meshes, which the compiler turns into NoShadows
                if (SkipEnd(ref name, "_nsh"))
                {
                    objectFlags |= ObjectTypeFlags.NoShadows;
                }

                // Older compilers wrote _nzp
                if (SkipEnd(ref name, "_nz") || SkipEnd(ref name, "_nzp"))
                {
                    flags |= WorldNodeExtraFlags.NoZPrepass;
                }

                if (SkipEnd(ref name, "_b"))
                {
                    objectFlags |= ObjectTypeFlags.BakedGeometry;
                }

                var handshakes = name.LastIndexOf("_cm", StringComparison.Ordinal);
                if (handshakes >= 0)
                {
                    var suffix = name[(handshakes + 3)..];
                    if (ReadNumber(ref suffix, out var cube) && Skip(ref suffix, "_lp") && ReadNumber(ref suffix, out var probe) && suffix.IsEmpty)
                    {
                        name = name[..handshakes];
                        cubeMapHandshake = cube;
                        lightProbeHandshake = probe;
                    }
                }
            }

            var index = -1;
            if (className.Parts.HasFlag(ClassParts.Index) && !ReadNumber(ref name, out index))
            {
                return Unknown;
            }

            if (className.Parts.HasFlag(ClassParts.Shadow))
            {
                if (Skip(ref name, "_noshadow"))
                {
                    objectFlags |= ObjectTypeFlags.NoShadows;
                }
                else
                {
                    Skip(ref name, "_shadow");
                }
            }

            var sourceName = string.Empty;
            var hasSource = className.Parts.HasFlag(ClassParts.Source) || (className.Parts.HasFlag(ClassParts.OptionalSource) && !name.IsEmpty);
            if (hasSource)
            {
                if (!Skip(ref name, className.SourceSeparator) || name.IsEmpty)
                {
                    return Unknown;
                }

                sourceName = name.ToString();
                name = [];
            }

            if (!name.IsEmpty)
            {
                return Unknown;
            }

            return new()
            {
                Type = className.Type,
                Flags = flags,
                Node = node,
                Layer = layer,
                Cluster = cluster,
                Index = index,
                SourceName = sourceName,
                ObjectFlags = objectFlags,
                CubeMapHandshake = cubeMapHandshake,
                LightProbeHandshake = lightProbeHandshake,
            };
        }

        // {node}_lr{layer}_agg_{kind}_{material}_{index}
        private static WorldNodeModelName ParseAggregate(ReadOnlySpan<char> name, int node, int layer)
        {
            var type = WorldNodeTypes.Unknown;
            foreach (var (kind, kindType) in AggregateKinds)
            {
                if (name.StartsWith(kind, StringComparison.Ordinal) && name[kind.Length..].StartsWith('_'))
                {
                    type = kindType;
                    name = name[(kind.Length + 1)..];
                    break;
                }
            }

            var indexStart = name.LastIndexOf('_');
            if (type == WorldNodeTypes.Unknown || indexStart < 0
                || !int.TryParse(name[(indexStart + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            {
                return Unknown;
            }

            return new()
            {
                Type = type,
                Node = node,
                Layer = layer,
                Index = index,
                SourceName = name[..indexStart].ToString(),
            };
        }

        // Older compilers named aggregates node000_world_lr0_agg12_34_material, with world, model, prop{id} or no kind
        private static WorldNodeModelName ParseLegacyAggregate(ReadOnlySpan<char> name, int node)
        {
            var kindLength = Skip(ref name, "_") ? name.IndexOf("_lr", StringComparison.Ordinal) : -1;
            if (kindLength < 0)
            {
                return Unknown;
            }

            var kind = name[..kindLength];
            name = name[kindLength..];

            // Props show up in world aggregates too, so model and prop only roughly mean baked props
            WorldNodeTypes type;
            if (kind is "model" || (kind.StartsWith("prop", StringComparison.Ordinal) && !kind[4..].ContainsAnyExceptInRange('0', '9')))
            {
                type = WorldNodeTypes.BakedPropLod;
            }
            else if (kind is "world" or "")
            {
                type = WorldNodeTypes.Aggregate;
            }
            else
            {
                return Unknown;
            }

            if (!Skip(ref name, "_lr") || !ReadNumber(ref name, out var layer)
                || !Skip(ref name, "_agg") || !ReadNumber(ref name, out var index)
                || !Skip(ref name, "_") || !ReadNumber(ref name, out _)
                || !Skip(ref name, "_") || name.IsEmpty)
            {
                return Unknown;
            }

            return new()
            {
                Type = type,
                Node = node,
                Layer = layer,
                Index = index,
                SourceName = name.ToString(),
            };
        }

        private static ClassName? FindClassName(ReadOnlySpan<char> name)
        {
            foreach (var className in ClassNames)
            {
                if (name.StartsWith(className.Prefix, StringComparison.Ordinal))
                {
                    return className;
                }
            }

            return null;
        }

        private static bool ReadTag(ref ReadOnlySpan<char> name, ref ObjectTypeFlags objectFlags, ref WorldNodeExtraFlags flags)
        {
            foreach (var (tag, objectFlag, flag) in Tags)
            {
                if (Skip(ref name, tag))
                {
                    objectFlags |= objectFlag;
                    flags |= flag;
                    return true;
                }
            }

            return false;
        }

        private static bool Skip(ref ReadOnlySpan<char> name, string prefix)
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            name = name[prefix.Length..];
            return true;
        }

        private static bool SkipEnd(ref ReadOnlySpan<char> name, string suffix)
        {
            if (!name.EndsWith(suffix, StringComparison.Ordinal))
            {
                return false;
            }

            name = name[..^suffix.Length];
            return true;
        }

        // Handshakes may be negative
        private static bool ReadNumber(ref ReadOnlySpan<char> name, out int value)
        {
            var length = name.StartsWith('-') ? 1 : 0;
            while (length < name.Length && char.IsAsciiDigit(name[length]))
            {
                length++;
            }

            if (!int.TryParse(name[..length], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value))
            {
                return false;
            }

            name = name[length..];
            return true;
        }
    }
}
