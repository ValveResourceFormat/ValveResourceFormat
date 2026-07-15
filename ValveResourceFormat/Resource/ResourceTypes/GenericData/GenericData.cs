using ValveResourceFormat.ResourceTypes.GenericData.CS2;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.GenericData;

/// <summary>
/// Base class for typed game data (<c>vdata</c>) DATA blocks, identified by their
/// <c>generic_data_type</c> value. Wraps an already-read <see cref="BinaryKV3"/> block and
/// exposes its parsed <see cref="BinaryKV3.Data"/> without re-reading it.
/// </summary>
public abstract class GenericData : BinaryKV3
{
    /// <summary>
    /// The <c>generic_data_type</c> value at the KV3 root of this block.
    /// </summary>
    public string GenericDataType { get; }

    /// <summary>
    /// Adopts the state of an already-read <see cref="BinaryKV3"/> DATA block.
    /// </summary>
    protected GenericData(BinaryKV3 kv3)
    {
        Offset = kv3.Offset;
        Size = kv3.Size;
        Data = kv3.Data;

        GenericDataType = GetGenericDataType(kv3);
    }

    /// <summary>
    /// Reads the <c>generic_data_type</c> value from a KV3 block.
    /// </summary>
    public static string GetGenericDataType(BinaryKV3 block)
    {
        return block.Data.Root.GetStringProperty("generic_data_type");
    }

    /// <summary>
    /// Constructs the typed game data block matching the <c>generic_data_type</c> of the given
    /// KV3 block, or <see langword="null"/> if there is no specialized type for it.
    /// </summary>
    public static GenericData? Construct(BinaryKV3 kv3)
    {
        return GetGenericDataType(kv3) switch
        {
            BombDamage.DataType => new BombDamage(kv3) { Resource = kv3.Resource },
            _ => null,
        };
    }
}
