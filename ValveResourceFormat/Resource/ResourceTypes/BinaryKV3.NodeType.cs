namespace ValveResourceFormat.ResourceTypes
{
    public partial class BinaryKV3
    {
#pragma warning disable CA1028 // Enum Storage should be Int32
        private enum KV3BinaryNodeType : byte
#pragma warning restore CA1028
        {
            NULL = 1,
            BOOLEAN = 2,
            INT64 = 3,
            UINT64 = 4,
            DOUBLE = 5,
            STRING = 6,
            BINARY_BLOB = 7,
            ARRAY = 8,
            OBJECT = 9,
            ARRAY_TYPED = 10,
            INT32 = 11,
            UINT32 = 12,
            BOOLEAN_TRUE = 13,
            BOOLEAN_FALSE = 14,
            INT64_ZERO = 15,
            INT64_ONE = 16,
            DOUBLE_ZERO = 17,
            DOUBLE_ONE = 18,
            FLOAT = 19,
            INT16 = 20,
            UINT16 = 21,
            UNKNOWN_22 = 22,
            INT32_AS_BYTE = 23,
            ARRAY_TYPE_BYTE_LENGTH = 24,
            ARRAY_TYPE_AUXILIARY_BUFFER = 25,
        }
    }
}
