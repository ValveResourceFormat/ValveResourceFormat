namespace ValveResourceFormat
{
    /// <summary>
    /// When <see cref="Resource.Read(System.IO.Stream, bool, BlockParsing)"/> parses the blocks of a resource.
    /// </summary>
    public enum BlockParsing
    {
        /// <summary>
        /// Every block the resource type uses at runtime parses in Read. Data only tools use, such as the edit info
        /// of most types, parses on first access.
        /// </summary>
        Runtime,

        /// <summary>
        /// Blocks parse on first access, so a block that fails to parse throws there instead of in Read. Only the DATA
        /// block of VData, which is specialized by its contents, and an edit info that determines the resource type parse in Read.
        /// </summary>
        Deferred,
    }
}
