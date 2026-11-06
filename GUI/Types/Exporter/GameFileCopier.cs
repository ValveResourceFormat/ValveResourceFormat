using System.IO;
using ValveResourceFormat.GoldSrc;
using ValveResourceFormat.Source1;
using ValveResourceFormat.Source1.FileSystem;

namespace GUI.Types.Exporter
{
    /// <summary>
    /// Copies the files of a Source 1 or GoldSrc game that a conversion opens into a folder laid out like the game,
    /// so the folder can be opened as that game later.
    /// </summary>
    sealed class GameFileCopier(string outputDirectory)
    {
        private enum Game
        {
            None,
            Source1,
            GoldSrc,
        }

        private const string GameInfoFileName = "gameinfo.txt";
        private const string LibListFileName = "liblist.gam";
        private const string GoldSrcBaseGameFolder = "valve";

        private readonly HashSet<string> copied = new(StringComparer.OrdinalIgnoreCase);
        private Game game;
        private string modFolder = string.Empty;
        private string? gameInfoPath;
        private string goldSrcRootDirectory = string.Empty;

        /// <summary>Gets the number of files copied.</summary>
        public int Count
        {
            get
            {
                lock (copied)
                {
                    return copied.Count;
                }
            }
        }

        /// <summary>Copies the files that <paramref name="fileSystem"/> opens from folders and packages.</summary>
        public void Attach(Source1FileSystem fileSystem)
        {
            game = Game.Source1;
            modFolder = Path.GetFileName(fileSystem.ModDirectory);
            gameInfoPath = fileSystem.GameInfoPath;
            fileSystem.FileOpened = CopyStream;
        }

        /// <summary>Copies the files that <paramref name="fileSystem"/> finds.</summary>
        public void Attach(GoldSrcFileSystem fileSystem)
        {
            game = Game.GoldSrc;
            modFolder = fileSystem.ModDirectory;
            goldSrcRootDirectory = fileSystem.RootDirectory;
            fileSystem.FileFound = CopyFile;
        }

        /// <summary>
        /// Copies the file that makes the folder a game of its own: the <c>gameinfo.txt</c> of the game for Source 1,
        /// and the <c>liblist.gam</c> of the mod for GoldSrc.
        /// </summary>
        public void Finish()
        {
            switch (game)
            {
                case Game.Source1:
                    FinishSource1();
                    break;

                case Game.GoldSrc:
                    FinishGoldSrc();
                    break;
            }
        }

        // Without a gameinfo.txt the game is a loose folder, which is found by its materials, models or maps folder
        private void FinishSource1()
        {
            if (gameInfoPath == null)
            {
                return;
            }

            var path = Path.Join(outputDirectory, modFolder, GameInfoFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(gameInfoPath, path, overwrite: true);
        }

        private void FinishGoldSrc()
        {
            // A game is found by the base game folder that sits next to it
            Directory.CreateDirectory(Path.Join(outputDirectory, GoldSrcBaseGameFolder));
            CopyFile(Path.Join(goldSrcRootDirectory, modFolder, LibListFileName));
        }

        private void CopyStream(string gamePath, Stream stream)
        {
            var path = Path.Join(outputDirectory, modFolder, Source1Paths.Normalize(gamePath));

            if (!TryReserve(path))
            {
                return;
            }

            var start = stream.Position;

            using (var file = File.Create(path))
            {
                stream.CopyTo(file);
            }

            stream.Position = start;
        }

        /// <summary>Copies a file of a GoldSrc game from disk to the same place of the output folder.</summary>
        public void CopyFile(string diskPath)
        {
            if (!File.Exists(diskPath))
            {
                return;
            }

            var relativePath = Path.GetRelativePath(goldSrcRootDirectory, diskPath);
            var path = Path.Join(outputDirectory, relativePath);

            if (TryReserve(path))
            {
                File.Copy(diskPath, path, overwrite: true);
            }
        }

        // Claims the output path for the calling thread, so a file that several threads open is written once
        private bool TryReserve(string path)
        {
            lock (copied)
            {
                if (!copied.Add(path))
                {
                    return false;
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            return true;
        }
    }
}
