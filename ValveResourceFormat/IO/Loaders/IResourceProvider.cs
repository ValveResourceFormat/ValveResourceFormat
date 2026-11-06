using System.IO;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.IO.Fgd;

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
    }
}
