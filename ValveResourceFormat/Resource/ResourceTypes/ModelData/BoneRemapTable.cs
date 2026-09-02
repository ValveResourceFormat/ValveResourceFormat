using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelData
{
    /// <summary>
    /// Maps the bone indices a mesh's <c>BLENDINDICES</c> carry to bone indices of the model skeleton.
    /// Every mesh owns a contiguous slice of one table.
    /// </summary>
    public sealed class BoneRemapTable
    {
        private readonly int[] starts;
        private readonly int[] table;

        /// <summary>
        /// Gets every mesh bone's skeleton bone index, all meshes concatenated. A mesh indexes its own
        /// slice rather than this.
        /// </summary>
        public ReadOnlyMemory<int> Table => table;

        /// <summary>Gets the number of meshes the table has a slice for.</summary>
        public int MeshCount => starts.Length;

        /// <summary>
        /// Initializes a bone remap table from a compiled model's data block.
        /// </summary>
        public BoneRemapTable(KVObject data)
        {
            ArgumentNullException.ThrowIfNull(data);

            var compiledStarts = data.GetIntegerArray("m_remappingTableStarts");
            var compiledTable = data.GetIntegerArray("m_remappingTable");

            starts = new int[compiledStarts.Length];
            for (var i = 0; i < compiledStarts.Length; i++)
            {
                starts[i] = (int)compiledStarts[i];
            }

            table = new int[compiledTable.Length];
            for (var i = 0; i < compiledTable.Length; i++)
            {
                table[i] = (int)compiledTable[i];
            }
        }

        /// <summary>
        /// Gets where a mesh's bones start in <see cref="Table"/>, or zero when the mesh has no slice.
        /// </summary>
        public int GetMeshStart(int meshIndex)
            => meshIndex >= 0 && meshIndex < starts.Length ? starts[meshIndex] : 0;

        /// <summary>
        /// Gets how many bones a mesh addresses, or zero when the mesh has no slice.
        /// </summary>
        public int GetMeshBoneCount(int meshIndex)
        {
            if (meshIndex < 0 || meshIndex >= starts.Length)
            {
                return 0;
            }

            var next = meshIndex + 1 < starts.Length ? starts[meshIndex + 1] : table.Length;

            return next - starts[meshIndex];
        }

        /// <summary>
        /// Gets a mesh's own slice of the table, or <see langword="null"/> when the mesh has no slice.
        /// </summary>
        public int[]? GetMeshTable(int meshIndex)
        {
            if (meshIndex < 0 || meshIndex >= starts.Length)
            {
                return null;
            }

            return table[GetMeshStart(meshIndex)..(GetMeshStart(meshIndex) + GetMeshBoneCount(meshIndex))];
        }
    }
}
