using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Source1.Entities;

internal static class Source1EntityCopy
{
    /// <summary>
    /// Copies an entity with its connections, and with its sound waves named as Source 2 sounds.
    /// </summary>
    /// <param name="source">The entity, which is not changed.</param>
    /// <param name="omittedKey">A key to leave out of the copy, or null.</param>
    /// <returns>The copy, whose connections come from the copy.</returns>
    public static EntityLump.Entity Copy(EntityLump.Entity source, string? omittedKey = null)
    {
        var entity = new EntityLump.Entity { ParentLump = source.ParentLump };

        foreach (var (key, value) in source.Children)
        {
            if (key == omittedKey)
            {
                continue;
            }

            if (value.ValueType == KVValueType.String && Source1Paths.IsWaveName((string)value))
            {
                entity[key] = Source1Paths.ToSoundPath((string)value);
            }
            else
            {
                entity[key] = value;
            }
        }

        if (source.Connections == null)
        {
            return entity;
        }

        var connections = new List<EntityLump.Connection>(source.Connections.Count);

        foreach (var connection in source.Connections)
        {
            var parameter = connection.OverrideParam;

            if (Source1Paths.IsWaveName(parameter))
            {
                parameter = Source1Paths.ToSoundPath(parameter);
            }

            connections.Add(new EntityLump.Connection
            {
                SourceEntity = entity,
                OutputName = connection.OutputName,
                InputName = connection.InputName,
                TargetName = connection.TargetName,
                OverrideParam = parameter,
                Delay = connection.Delay,
                TimesToFire = connection.TimesToFire,
                TargetType = connection.TargetType,
                ParamMap = connection.ParamMap,
            });
        }

        entity.Connections = connections;

        return entity;
    }
}
