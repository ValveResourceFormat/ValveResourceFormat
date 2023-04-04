using System.IO;
using System.Collections.Generic;
using ValveResourceFormat.IO.Formats.ValveMap;
using ValveResourceFormat.Utils;
using ValveResourceFormat.ResourceTypes;
using System.Numerics;
using System;
using System.Text;

namespace ValveResourceFormat.IO;

public sealed class MapExtract
{
    private string MapName { get; }
    private string MapRoot { get; }

    private string LumpFolder => Path.Combine(MapRoot, MapName);

    private List<string> AssetReferences { get; } = new List<string>();
    private CMapRootElement Vmap { get; } = new();

    private string FolderName => MapName + "_d";

    private readonly IFileLoader FileLoader;

    private readonly Dictionary<uint, string> HashTable = StringToken.InvertedTable;
    static class CommonHashes
    {
        public static readonly uint ClassName = StringToken.Get("classname");
        public static readonly uint Origin = StringToken.Get("origin");
        public static readonly uint Angles = StringToken.Get("angles");
        public static readonly uint Scales = StringToken.Get("scales");
    }

    public MapExtract(Resource vmap, IFileLoader fileLoader)
    {
        MapName = Path.GetFileNameWithoutExtension(vmap.FileName);
        MapRoot = Path.GetDirectoryName(vmap.FileName);
        FileLoader = fileLoader;

        AssetReferences = new List<string>();

        if (fileLoader is not null)
        {
            using (var vwrld = fileLoader.LoadFile(Path.Combine(LumpFolder, "world.vwrld_c")))
            {
                ((World)vwrld.DataBlock).GetEntityLumpNames();
                ((World)vwrld.DataBlock).GetWorldNodeNames();
            }
        }
    }

    public ContentFile ToContentFile()
    {
        var vmap = new ContentFile
        {
            Data = Encoding.UTF8.GetBytes(ToValveMap()),
        };

        return vmap;
    }

    public string ToValveMap()
    {
        using var datamodel = new Datamodel.Datamodel("vmap", 29);

        datamodel.PrefixAttributes.Add("map_asset_references", AssetReferences);
        datamodel.Root = Vmap;

        GatherEntities();
        AddWorldNodesAsStaticProps();

        using var stream = new MemoryStream();
        datamodel.Save(stream, "keyvalues2", 4);

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private void GatherEntities()
    {
        using var vents = FileLoader.LoadFile(Path.Combine(LumpFolder, "entities", "default_ents.vents_c"));
        var entities = ((EntityLump)vents.DataBlock).GetEntities();

        foreach (var entity in entities)
        {
            FixUpEntityKeyValues(entity);

            if (entity.GetProperty<string>(CommonHashes.ClassName) == "worldspawn")
            {
                AddProperties(Vmap.World, entity);
                Vmap.World.EntityProperties["description"] = $"Decompiled with VRF 0.3.2 - https://vrf.steamdb.info/";
                continue;
            }

            var childEnt = new CMapEntity();
            AddProperties(childEnt, entity);

            Vmap.World.Children.Add(childEnt);
        }
    }

    private void AddWorldNodesAsStaticProps()
    {
        using var vwnod = FileLoader.LoadFile(Path.Combine(LumpFolder, "worldnodes", "node000.vwnod_c"));
        var node = (WorldNode)vwnod.DataBlock;

        foreach (var sceneObject in node.SceneObjects)
        {
            var modelName = sceneObject.GetProperty<string>("m_renderableModel");
            using (var vmdl = FileLoader.LoadFile(modelName + "_c"))
            {
                if (vmdl is null)
                {
                    Console.WriteLine("Failed to load resource: {0} (ERROR_FILEOPEN)", modelName);
                    continue;
                }
            }

            var propStatic = new CMapEntity();
            propStatic.EntityProperties["classname"] = "prop_static";
            propStatic.EntityProperties["model"] = modelName;

            if (modelName.Contains("nomerge", StringComparison.Ordinal))
            {
                propStatic.EntityProperties["disablemeshmerging"] = "1";
            }

            Console.WriteLine("Added model to map: {0}", modelName);
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
