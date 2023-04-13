using System.IO;
using System.Collections.Generic;
using ValveResourceFormat.IO.Formats.ValveMap;
using ValveResourceFormat.Utils;
using ValveResourceFormat.ResourceTypes;
using System.Numerics;
using System;
using System.Text;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Blocks;

namespace ValveResourceFormat.IO;

public sealed class MapExtract
{
    public string MapName { get; private set; }
    private string MapRoot { get; set; }

    private string LumpFolder => Path.Combine(MapRoot, MapName);

    private IReadOnlyCollection<string> EntityLumpNames { get; set; }
    private IReadOnlyCollection<string> WorldNodeNames { get; set; }

    private List<string> AssetReferences { get; } = new List<string>();
    private List<string> FilesForExtract { get; } = new List<string>();
    private CMapRootElement Vmap { get; } = new();

    private string FolderName => MapName + "_d";

    private readonly IFileLoader FileLoader;

    public IProgress<string> ProgressReporter { get; set; }

    private readonly Dictionary<uint, string> HashTable = StringToken.InvertedTable;

    internal static class CommonHashes
    {
        public static readonly uint ClassName = StringToken.Get("classname");
        public static readonly uint Origin = StringToken.Get("origin");
        public static readonly uint Angles = StringToken.Get("angles");
        public static readonly uint Scales = StringToken.Get("scales");
    }

    /// <summary>
    /// Extract a map from a resource. Accepted types include Map, World, WorldNode and EntityLump.
    /// </summary>
    public MapExtract(Resource resource, IFileLoader fileLoader)
    {
        FileLoader = fileLoader;

        switch (resource.ResourceType)
        {
            case ResourceType.Map:
                InitMapExtract(resource);
                break;
            default:
                throw new InvalidDataException($"Resource type {resource.ResourceType} is not supported for map extraction.");
        }
    }

    /// <summary>
    /// Extract a map by name and a vpk-based file loader.
    /// </summary>
    public MapExtract(string mapNameFull, IFileLoader fileLoader)
    {
        FileLoader = fileLoader;

        MapName = Path.GetFileNameWithoutExtension(mapNameFull);
        MapRoot = Path.GetDirectoryName(mapNameFull);
    }

    private void InitMapExtract(Resource vmap)
    {
        MapName = Path.GetFileNameWithoutExtension(vmap.FileName);
        MapRoot = Path.GetDirectoryName(vmap.FileName);

        if (string.IsNullOrEmpty(MapRoot))
        {
            CollectMapNameFromVmapRERL(vmap.ExternalReferences);
        }

        var worldPath = Path.Combine(LumpFolder, "world.vwrld_c");
        using var worldResource = FileLoader?.LoadFile(worldPath);

        if (worldResource == null)
        {
            throw new FileNotFoundException($"Failed to find {worldPath}.");
        }

        var world = (World)worldResource.DataBlock;
        EntityLumpNames = world.GetEntityLumpNames();
        WorldNodeNames = world.GetWorldNodeNames();
    }

    private void CollectMapNameFromVmapRERL(ResourceExtRefList rerl)
    {
        foreach (var info in rerl.ResourceRefInfoList)
        {
            if (info.Name.EndsWith("world.vrman", StringComparison.OrdinalIgnoreCase))
            {
                CollectMapNameFromWorldPath(info.Name);
                return;
            }
        }

        throw new InvalidDataException("Could not find world.vrman in RERL.");
    }

    private void InitWorldExtract(Resource vworld)
    {
        CollectMapNameFromWorldPath(vworld.FileName);
    }

    private void CollectMapNameFromWorldPath(string worldPath)
    {
        MapName = Directory.GetParent(worldPath).Name;
        MapRoot = Path.GetDirectoryName(Path.GetDirectoryName(worldPath));
    }

    public ContentFile ToContentFile()
    {
        var vmap = new ContentFile
        {
            Data = Encoding.UTF8.GetBytes(ToValveMap()),
        };

        vmap.RequiredGameFiles.AddRange(FilesForExtract);

        return vmap;
    }

    public string ToValveMap()
    {
        using var datamodel = new Datamodel.Datamodel("vmap", 29);

        datamodel.PrefixAttributes.Add("map_asset_references", AssetReferences);
        datamodel.Root = Vmap;

        foreach (var entityLumpName in EntityLumpNames)
        {
            using var entityLump = FileLoader.LoadFile(entityLumpName + "_c");
            if (entityLump is not null)
            {
                GatherEntitiesFromLump((EntityLump)entityLump.DataBlock);
            }
        }

        foreach (var worldNodeName in WorldNodeNames)
        {
            using var worldNode = FileLoader.LoadFile(worldNodeName + ".vwnod_c");
            if (worldNode is not null)
            {
                AddWorldNodesAsStaticProps((WorldNode)worldNode.DataBlock);
            }
        }

        using var stream = new MemoryStream();
        datamodel.Save(stream, "keyvalues2", 4);

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private void GatherEntitiesFromLump(EntityLump entityLump)
    {
        foreach (var compiledEntity in entityLump.GetEntities())
        {
            FixUpEntityKeyValues(compiledEntity);

            if (compiledEntity.GetProperty<string>(CommonHashes.ClassName) == "worldspawn")
            {
                AddProperties(Vmap.World, compiledEntity);
                Vmap.World.EntityProperties["description"] = $"Decompiled with VRF 0.3.2 - https://vrf.steamdb.info/";
                continue;
            }

            var mapEntity = new CMapEntity();
            AddProperties(mapEntity, compiledEntity);

            Vmap.World.Children.Add(mapEntity);
        }
    }

    private void AddWorldNodesAsStaticProps(WorldNode node)
    {
        foreach (var sceneObject in node.SceneObjects)
        {
            var modelName = sceneObject.GetProperty<string>("m_renderableModel");
            var meshName = sceneObject.GetProperty<string>("m_renderable");

            var fadeStartDistance = sceneObject.GetProperty<double>("m_flFadeStartDistance");
            var fadeEndDistance = sceneObject.GetProperty<double>("m_flFadeEndDistance");
            var tintColor = sceneObject.GetSubCollection("m_vTintColor").ToVector4();
            var skin = sceneObject.GetProperty<string>("m_skin");

            var objectFlags = ObjectTypeFlags.None;
            try
            {
                objectFlags = (ObjectTypeFlags)sceneObject.GetProperty<int>("m_nObjectTypeFlags");
            }
            catch (InvalidCastException)
            {
                // TODO: Parse from string
            }

            if (modelName is null)
            {
                // TODO: Generate model for mesh
                continue;
            }

            var propStatic = new CMapEntity();
            propStatic.EntityProperties["classname"] = "prop_static";
            propStatic.EntityProperties["model"] = modelName;
            //propStatic.EntityProperties["fademindist"] = fadeStartDistance.ToString(CultureInfo.InvariantCulture);
            //propStatic.EntityProperties["fademaxdist"] = fadeEndDistance.ToString(CultureInfo.InvariantCulture);
            //propStatic.EntityProperties["rendercolor"] = $"{tintColor.X} {tintColor.Y} {tintColor.Z}";
            //propStatic.EntityProperties["renderamt"] = tintColor.W.ToString(CultureInfo.InvariantCulture);
            propStatic.EntityProperties["skin"] = string.IsNullOrEmpty(skin) ? "default" : skin;

            if ((objectFlags & ObjectTypeFlags.RenderToCubemaps) != 0)
            {
                propStatic.EntityProperties["rendertocubemaps"] = "1";
            }

            if ((objectFlags & ObjectTypeFlags.NoShadows) != 0)
            {
                propStatic.EntityProperties["disableshadows"] = "1";
            }

            if (Path.GetFileName(modelName).Contains("nomerge", StringComparison.Ordinal))
            {
                propStatic.EntityProperties["disablemeshmerging"] = "1";
            }

            Vmap.World.Children.Add(propStatic);
        }
    }

    private void FixUpEntityKeyValues(EntityLump.Entity entity)
    {
        foreach (var (hash, property) in entity.Properties)
        {
            property.Name ??= HashTable.GetValueOrDefault(hash, $"{hash}");
        }
    }

    private static void AddProperties(BaseEntity mapEntity, EntityLump.Entity entity)
    {
        foreach (var (hash, property) in entity.Properties)
        {
            if (property.Name is null)
            {
                continue;
            }

            if (hash == CommonHashes.Origin)
            {
                mapEntity.Origin = GetVector3Property(property);
                continue;
            }
            else if (hash == CommonHashes.Angles)
            {
                mapEntity.Angles = GetVector3Property(property);
                continue;
            }
            else if (hash == CommonHashes.Scales)
            {
                mapEntity.Scales = GetVector3Property(property);
                continue;
            }

            mapEntity.EntityProperties.Add(property.Name, PropertyToEditString(property));
        }
    }

    private static Vector3 GetVector3Property(EntityLump.EntityProperty property)
    {
        return property.Type switch
        {
            EntityFieldType.CString or EntityFieldType.String => EntityTransformHelper.ParseVector((string)property.Data),
            EntityFieldType.Vector or EntityFieldType.QAngle => (Vector3)property.Data,
            _ => throw new InvalidDataException("Unhandled Entity Vector Data Type!"),
        };
    }

    private static string PropertyToEditString(EntityLump.EntityProperty property)
    {
        var type = property.Type;
        var data = property.Data;
        return data switch
        {
            string str => str,
            bool boolean => boolean ? "1" : "0",
            Vector3 vector => $"{vector.X} {vector.Y} {vector.Z}",
            byte[] color => $"{color[0]} {color[1]} {color[2]} {color[3]}",
            _ => data.ToString()
        };
    }
}
