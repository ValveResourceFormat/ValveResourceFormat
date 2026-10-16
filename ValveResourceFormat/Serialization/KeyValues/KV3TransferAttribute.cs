namespace ValveResourceFormat.Serialization.KeyValues;

/// <summary>
/// Generates a constructor reading the type's auto-properties from KeyValues3 data.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
internal sealed class KV3TransferAttribute : Attribute
{
}
