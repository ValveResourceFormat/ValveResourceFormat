using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.NavMesh
{
    /// <summary>
    /// Reads the names of the navigation hulls from the game's hull presets.
    /// </summary>
    public static class NavHullNames
    {
        /// <summary>
        /// Path of the hull presets file, which lists the hull names of each preset in hull index order.
        /// </summary>
        public const string PresetsFile = "scripts/nav_hulls_presets.vdata_c";

        /// <summary>
        /// Gets the hull names of a preset, in hull index order.
        /// </summary>
        /// <param name="fileLoader">The loader to read the presets file with.</param>
        /// <param name="presetName">The preset name stored in <see cref="NavMeshGenerationParams.HullPresetName"/>, or empty for the default preset.</param>
        /// <returns>The hull names, or an empty array when the game has no presets file or no such preset.</returns>
        public static string[] Read(IFileLoader fileLoader, string? presetName)
        {
            using var presets = fileLoader.LoadFile(PresetsFile);

            if (presets?.DataBlock is not BinaryKV3 kv3)
            {
                return [];
            }

            var preset = kv3.Data.Root.GetSubCollection(string.IsNullOrEmpty(presetName) ? "default" : presetName);

            return preset?.GetArray<string>("m_vecNavHulls") ?? [];
        }

        /// <summary>
        /// Gets the name of a hull, or <c>hull N</c> when it is not named.
        /// </summary>
        public static string GetName(IReadOnlyList<string> hullNames, int hullIndex)
            => hullIndex < hullNames.Count ? hullNames[hullIndex] : $"hull {hullIndex}";
    }
}
