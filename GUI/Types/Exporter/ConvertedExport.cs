using System.IO;
using GUI.Types.Viewers;
using GUI.Utils;
using ValveResourceFormat.GoldSrc;
using ValveResourceFormat.Source1.FileSystem;
using Resource = ValveResourceFormat.Resource;
using ResourceType = ValveResourceFormat.ResourceType;

namespace GUI.Types.Exporter
{
    /// <summary>
    /// A Source 1 or GoldSrc file converted to a Source 2 resource, with the context that serves the other files of its game.
    /// </summary>
    sealed class ConvertedExport : IDisposable
    {
        /// <summary>The kind of file a stream holds, found from its header and name.</summary>
        /// <param name="Magic">The first four bytes of the file.</param>
        /// <param name="IsGoldSrc">Whether the file is a GoldSrc file, rather than a Source 1 file.</param>
        /// <param name="ResourceType">The type of the resource the file converts to, or <see cref="ResourceType.Unknown"/> when it is not converted.</param>
        public readonly record struct Format(uint Magic, bool IsGoldSrc, ResourceType ResourceType)
        {
            /// <summary>Gets whether the file is a Source 1 or GoldSrc texture, material, model, sprite or map, which <see cref="TryConvert"/> converts.</summary>
            public bool IsSupported => ResourceType != ResourceType.Unknown;
        }

        private readonly Source1FileSystem? ownedFileSystem;

        public Resource Resource { get; }

        /// <summary>Gets the context to load the files that the resource refers to with.</summary>
        public VrfGuiContext Context { get; }

        private ConvertedExport(Resource resource, VrfGuiContext context, Source1FileSystem? ownedFileSystem)
        {
            Resource = resource;
            Context = context;
            this.ownedFileSystem = ownedFileSystem;
        }

        /// <summary>
        /// Finds out whether the file is converted, and to what.
        /// </summary>
        /// <param name="fileName">The path of the file inside of its package, or its name when it is on disk.</param>
        /// <param name="stream">The stream of the file, which is left where it was.</param>
        public static Format GetFormat(string fileName, Stream stream)
        {
            if (stream.Length - stream.Position < ViewerFactory.HeaderLength)
            {
                return default;
            }

            var (magic, version) = ViewerFactory.ReadHeader(stream);

            var goldSrcType = GoldSrcContent.GetResourceType(magic, version, fileName);

            if (goldSrcType != ResourceType.Unknown)
            {
                return new Format(magic, true, goldSrcType);
            }

            return new Format(magic, false, Source1Content.GetResourceType(magic, fileName));
        }

        /// <summary>
        /// Converts the file when it is a Source 1 or GoldSrc texture, material, model, sprite or map.
        /// </summary>
        /// <param name="fileName">The path of the file inside of its package, or its name when it is on disk.</param>
        /// <param name="stream">The stream of the file.</param>
        /// <param name="parentContext">The context of the package the file is in, or of the file itself when it is on disk.</param>
        /// <param name="copier">Receives the file and the files of its game that the conversion opens, or <see langword="null"/>.</param>
        /// <returns>The converted file, or <see langword="null"/> when the file is not one of those.</returns>
        public static ConvertedExport? TryConvert(string fileName, Stream stream, VrfGuiContext parentContext, GameFileCopier? copier = null)
        {
            var format = GetFormat(fileName, stream);

            return format.IsSupported ? Convert(fileName, stream, format, parentContext, copier) : null;
        }

        /// <summary>
        /// Converts a file that <see cref="GetFormat"/> found to be supported.
        /// </summary>
        /// <param name="fileName">The path of the file inside of its package, or its name when it is on disk.</param>
        /// <param name="stream">The stream of the file.</param>
        /// <param name="format">The format of the file.</param>
        /// <param name="parentContext">The context of the package the file is in, or of the file itself when it is on disk.</param>
        /// <param name="copier">Receives the file and the files of its game that the conversion opens, or <see langword="null"/>.</param>
        public static ConvertedExport Convert(string fileName, Stream stream, Format format, VrfGuiContext parentContext, GameFileCopier? copier = null)
        {
            // The conversion mounts the game on its context, which must not be the one of a tab that is showing the file
            var contextFileName = parentContext.CurrentPackage == null ? parentContext.FileName : fileName;
            var context = new VrfGuiContext(contextFileName, parentContext);

            try
            {
                if (format.IsGoldSrc)
                {
                    return new ConvertedExport(ConvertGoldSrc(context, stream, copier), context, null);
                }

                var converted = ConvertSource1(context, stream, format.Magic, copier);

                return new ConvertedExport(converted.Resource, context, converted.OwnedFileSystem);
            }
            catch
            {
                context.Dispose();
                throw;
            }
        }

        private static Source1Content.ConvertedFile ConvertSource1(VrfGuiContext context, Stream stream, uint magic, GameFileCopier? copier)
        {
            if (copier == null)
            {
                return Source1Content.Convert(context, stream, magic);
            }

            var fileSystem = Source1Content.Create(context)
                ?? throw new InvalidOperationException("The game of the file is not known.");

            try
            {
                copier.Attach(fileSystem);

                var gamePath = Source1Content.GetGamePath(context, fileSystem);
                fileSystem.ReportOpened(gamePath, stream);

                var converted = Source1Content.Convert(context, stream, magic, fileSystem);

                return new Source1Content.ConvertedFile(converted.Resource, fileSystem);
            }
            catch
            {
                fileSystem.Dispose();
                throw;
            }
        }

        private static Resource ConvertGoldSrc(VrfGuiContext context, Stream stream, GameFileCopier? copier)
        {
            if (copier == null)
            {
                return GoldSrcContent.Convert(context, stream);
            }

            var fileSystem = GoldSrcFileSystem.FromPath(Source1Content.GetDiskPath(context), VrfGuiContext.Logger)
                ?? throw new InvalidOperationException("The game of the file is not known.");
            copier.Attach(fileSystem);
            copier.CopyFile(context.FileName);

            return GoldSrcContent.Convert(context, stream, fileSystem);
        }

        public void Dispose()
        {
            Resource.Dispose();
            Context.Dispose();
            ownedFileSystem?.Dispose();
        }
    }
}
