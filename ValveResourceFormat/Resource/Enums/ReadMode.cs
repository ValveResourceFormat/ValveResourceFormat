namespace ValveResourceFormat
{
    /// <summary>
    /// When <see cref="Resource.Read(System.IO.Stream, bool, ReadMode)"/> parses the blocks of a resource.
    /// </summary>
    public enum ReadMode
    {
        /// <summary>
        /// Reads every block up front.
        /// </summary>
        Eager,

        /// <summary>
        /// Reads most blocks up front, and defers some tool-only blocks like Resource Edit Info.
        /// </summary>
        Runtime,

        /// <summary>
        /// Blocks parse on first access.
        /// </summary>
        Deferred,
    }
}
