namespace ValveResourceFormat.IO
{
    /// <summary>
    /// Optional interface of an <see cref="IFileLoader"/> that can write materials of another engine as Valve materials.
    /// </summary>
    public interface IMaterialExtractor
    {
        /// <summary>
        /// Writes a material of another engine as a Valve material with the images its textures need.
        /// </summary>
        /// <param name="material">The material.</param>
        /// <returns>The material file with its textures as additional files, or <see langword="null"/> to extract the material as it is.</returns>
        public ContentFile? ExtractMaterial(ResourceTypes.Material material);
    }
}
