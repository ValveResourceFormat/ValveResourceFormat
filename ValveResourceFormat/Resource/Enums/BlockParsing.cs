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
        /// Only blocks that others depend on, such as the introspection manifest, parse in Read. Every other block
        /// parses on first access, so a block that fails to parse throws there instead of in Read.
        /// </summary>
        Deferred,
    }
}
