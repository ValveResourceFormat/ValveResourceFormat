using System.IO;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.IO
{
    /// <summary>
    /// Serves resources that are not stored in the game packages, such as files of another engine converted to Source 2 resources.
    /// </summary>
    public interface IResourceProvider
    {
        /// <summary>
        /// Loads a resource the packages do not have.
        /// </summary>
        /// <param name="file">Path of the file as it is passed to <see cref="IFileLoader.LoadFile"/>, including the <b>"_c"</b> suffix of compiled files.</param>
        /// <returns>The resource, or <see langword="null"/> when the provider does not serve the path.</returns>
        public Resource? LoadFile(string file);

        /// <summary>
        /// Checks whether the provider serves a file, without loading it.
        /// </summary>
        /// <param name="file">Path of the file as it is passed to <see cref="IFileLoader.LoadFile"/>, including the <b>"_c"</b> suffix of compiled files.</param>
        /// <returns>Whether <see cref="LoadFile"/> finds the file.</returns>
        public bool Exists(string file);

        /// <summary>
        /// Opens a file that is not a resource, such as a script, which the packages do not have.
        /// </summary>
        /// <param name="file">Path of the file as it is passed to <see cref="IFileLoader.GetFileStream"/>.</param>
        /// <returns>The stream, or <see langword="null"/> when the provider does not serve the path.</returns>
        public Stream? GetFileStream(string file) => null;

        /// <summary>
        /// Gets the entity classes the maps this provider serves are shown with.
        /// </summary>
        /// <param name="logger">Where it is said which classes are used.</param>
        /// <returns>The classes, or <see langword="null"/> to find them from the game of the file loader.</returns>
        public EntityClassTable? GetEntityClasses(ILogger logger) => null;

        /// <summary>
        /// Writes a material this provider serves as a Valve material with the images its textures need.
        /// </summary>
        /// <param name="material">The material.</param>
        /// <returns>The material file with its textures as additional files, or <see langword="null"/> to extract the material as it is.</returns>
        public ContentFile? ExtractMaterial(ResourceTypes.Material material) => null;

        /// <summary>
        /// Gets the entities of an entity lump as they are written to a Hammer map.
        /// </summary>
        /// <param name="entities">The entities of the lump, which are not changed.</param>
        /// <returns>The entities to write, which may be copies.</returns>
        public List<EntityLump.Entity> GetExportEntities(List<EntityLump.Entity> entities) => entities;

        /// <summary>
        /// Adds the properties the map compiler requires to an entity written to a Hammer map.
        /// </summary>
        /// <param name="className">The class of the entity.</param>
        /// <param name="properties">The properties written for the entity.</param>
        public void AddExportProperties(string className, EditGameClassProps properties)
        {
        }

        /// <summary>
        /// Lists the models, textures, post processing and maps an entity lump refers to that a Hammer map carries, because they are not game files.
        /// </summary>
        /// <param name="entityLump">The entity lump resource.</param>
        /// <returns>The paths of the resources without the <b>"_c"</b> suffix.</returns>
        public IEnumerable<string> GetExportResources(Resource entityLump) => [];
    }
}
