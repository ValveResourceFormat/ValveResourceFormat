using System.Globalization;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.SmartProps;

namespace ValveResourceFormat.IO.ContentFormats.ValveMap;

/// <summary>Associates a source VMAP node id with its entity representation.</summary>
/// <param name="NodeId">The source VMAP node id.</param>
/// <param name="Entity">The entity and its I/O connections.</param>
public sealed record ValveMapEntity(int NodeId, EntityLump.Entity Entity);

/// <summary>Reads entity properties and I/O connections from source VMAP documents.</summary>
public static class ValveMapEntityReader
{
    /// <summary>Reads every entity below a VMAP root element.</summary>
    /// <param name="mapRoot">The VMAP document root.</param>
    /// <returns>The entities in map-tree order.</returns>
    public static IReadOnlyList<ValveMapEntity> ReadAll(Datamodel.Element mapRoot)
    {
        List<ValveMapEntity> entities = [];
        var parentLump = new EntityLump { Resource = new Resource() };
        if (mapRoot.TryGetValue("world", out var worldValue) && worldValue is Datamodel.Element world)
        {
            ReadElement(world, Matrix4x4.Identity, parentLump, entities);
        }

        return entities;
    }

    private static void ReadElement(
        Datamodel.Element element,
        Matrix4x4 parentTransform,
        EntityLump parentLump,
        List<ValveMapEntity> entities)
    {
        var worldTransform = ReadTransform(element) * parentTransform;
        if (TryReadEntity(element, worldTransform, parentLump) is { } entity)
        {
            entities.Add(entity);
        }

        if (!element.TryGetValue("children", out var childrenValue) || childrenValue is not Datamodel.ElementArray children)
        {
            return;
        }

        for (var i = 0; i < children.Count; i++)
        {
            if (children[i] is Datamodel.Element child)
            {
                ReadElement(child, worldTransform, parentLump, entities);
            }
        }
    }

    private static ValveMapEntity? TryReadEntity(Datamodel.Element element, Matrix4x4 worldTransform, EntityLump parentLump)
    {
        Datamodel.Element? properties = null;
        if (element.TryGetValue("entity_properties", out var propertiesValue)
            && propertiesValue is Datamodel.Element entityProperties
            && entityProperties.TryGetValue("classname", out var classValue)
            && classValue is string { Length: > 0 })
        {
            properties = entityProperties;
        }

        var smartProp = SmartPropMapParameters.Read(element);
        if (properties == null && smartProp == null)
        {
            return null;
        }

        var entity = new EntityLump.Entity { ParentLump = parentLump };
        if (properties != null)
        {
            foreach (var (name, value) in properties)
            {
                entity.Add(name.ToLowerInvariant(), ConvertValue(value));
            }
        }
        else
        {
            entity["classname"] = element.ClassName;
            entity["smartpropfilename"] = smartProp!.SmartPropFilename;
            entity["randomseed"] = smartProp.RandomSeed;
            foreach (var (name, value) in smartProp.Values)
            {
                entity[$"parameter.{name}"] = value;
            }
        }

        var nodeId = ReadInt32(element, "nodeID");
        Matrix4x4.Decompose(worldTransform, out var scales, out var rotation, out var origin);
        entity["hammeruniqueid"] = nodeId.ToString(CultureInfo.InvariantCulture);
        entity["origin"] = FormatVector(origin);
        entity["angles"] = FormatVector(EntityTransformHelper.ToEulerAngles(rotation));
        entity["scales"] = FormatVector(scales);
        entity.Connections = ReadConnections(element, entity);

        return new ValveMapEntity(nodeId, entity);
    }

    private static List<EntityLump.Connection>? ReadConnections(Datamodel.Element element, EntityLump.Entity entity)
    {
        if (!element.TryGetValue("connectionsData", out var connectionsValue)
            || connectionsValue is not Datamodel.ElementArray connectionElements
            || connectionElements.Count == 0)
        {
            return null;
        }

        List<EntityLump.Connection> connections = [];
        for (var i = 0; i < connectionElements.Count; i++)
        {
            if (connectionElements[i] is not Datamodel.Element connection)
            {
                continue;
            }

            connections.Add(new EntityLump.Connection
            {
                SourceEntity = entity,
                OutputName = ReadString(connection, "outputName"),
                InputName = ReadString(connection, "inputName"),
                TargetName = ReadString(connection, "targetName"),
                OverrideParam = ReadString(connection, "overrideParam"),
                Delay = ReadSingle(connection, "delay"),
                TimesToFire = ReadInt32(connection, "timesToFire", -1),
                TargetType = (EntityIOTargetType)ReadInt32(connection, "targetType"),
            });
        }

        return connections.Count > 0 ? connections : null;
    }

    private static KVObject ConvertValue(object? value)
        => value switch
        {
            null => KVObject.Null(),
            bool boolean => boolean,
            string text => text,
            int integer => integer,
            long integer => integer,
            uint integer => integer,
            ulong integer => integer,
            float number => number,
            double number => number,
            Vector2 vector => FormatVector(new Vector3(vector, 0f)),
            Vector3 vector => FormatVector(vector),
            Datamodel.QAngle angles => FormatVector(new Vector3(angles.Pitch, angles.Yaw, angles.Roll)),
            _ => value.ToString() ?? string.Empty,
        };

    private static Matrix4x4 ReadTransform(Datamodel.Element element)
    {
        var angles = Vector3.Zero;
        if (element.TryGetValue("angles", out var value) && value is Datamodel.QAngle qAngle)
        {
            angles = new Vector3(qAngle.Pitch, qAngle.Yaw, qAngle.Roll);
        }

        return Matrix4x4.CreateScale(ReadVector(element, "scales", Vector3.One))
            * EntityTransformHelper.EulerAnglesToRotationMatrix(angles)
            * Matrix4x4.CreateTranslation(ReadVector(element, "origin", Vector3.Zero));
    }

    private static string FormatVector(Vector3 vector)
        => string.Create(CultureInfo.InvariantCulture, $"{vector.X} {vector.Y} {vector.Z}");

    private static Vector3 ReadVector(Datamodel.Element element, string name, Vector3 fallback)
        => element.TryGetValue(name, out var value) && value is Vector3 vector ? vector : fallback;

    private static string ReadString(Datamodel.Element element, string name)
        => element.TryGetValue(name, out var value) && value is string text ? text : string.Empty;

    private static int ReadInt32(Datamodel.Element element, string name, int fallback = 0)
        => element.TryGetValue(name, out var value) && value is IConvertible convertible
            ? convertible.ToInt32(CultureInfo.InvariantCulture)
            : fallback;

    private static float ReadSingle(Datamodel.Element element, string name)
        => element.TryGetValue(name, out var value) && value is IConvertible convertible
            ? convertible.ToSingle(CultureInfo.InvariantCulture)
            : 0f;
}
