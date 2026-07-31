using System.Text;
using SteamDatabase.ValvePak;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.IO;

var games = GameFolderLocator.FindAllSteamGames();

var featureToShaders = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
var uniformToShaders = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
var shaderToGames = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);

foreach (var game in games)
{
    var vpkFiles = Directory.EnumerateFiles(game.GamePath, "shaders_*_dir.vpk", SearchOption.AllDirectories).ToList();

    if (vpkFiles.Count == 0)
    {
        continue;
    }

    Console.WriteLine($"Processing {game.AppName} (AppID {game.AppID})...");

    var seenShaders = new HashSet<string>(StringComparer.Ordinal);

    foreach (var vpkPath in vpkFiles)
    {
        using var package = new Package();
        package.SetFileName(vpkPath);

        try
        {
            using var stream = File.OpenRead(vpkPath);
            package.Read(stream);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Failed to read '{vpkPath}': {e.Message}");
            continue;
        }

        if (package.Entries == null || !package.Entries.TryGetValue("vcs", out var vcsEntries))
        {
            continue;
        }

        foreach (var entry in vcsEntries)
        {
            var fullPath = entry.GetFullPath();

            if (!fullPath.EndsWith("_features.vcs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string shaderName;

            try
            {
                (shaderName, _, _, _) = ShaderUtilHelpers.ComputeVCSFileName(fullPath);
            }
            catch (Exception)
            {
                continue;
            }

            if (!shaderToGames.TryGetValue(shaderName, out var gameSet))
            {
                gameSet = new SortedSet<string>(StringComparer.Ordinal);
                shaderToGames[shaderName] = gameSet;
            }

            gameSet.Add(game.AppName);

            if (!seenShaders.Add(shaderName))
            {
                continue;
            }

            try
            {
                package.ReadEntry(entry, out var bytes);

                using var shader = new VfxProgramData();
                using var memStream = new MemoryStream(bytes);
                shader.Read(fullPath, memStream);

                foreach (var combo in shader.StaticComboArray)
                {
                    if (!featureToShaders.TryGetValue(combo.Name, out var shaderSet))
                    {
                        shaderSet = new SortedSet<string>(StringComparer.Ordinal);
                        featureToShaders[combo.Name] = shaderSet;
                    }

                    shaderSet.Add(shaderName);
                }

                foreach (var variable in shader.VariableDescriptions)
                {
                    if (variable.VariableSource is not (VfxVariableSourceType.__SetByArtist__ or VfxVariableSourceType.__SetByArtistAndExpression__))
                    {
                        continue;
                    }

                    if (!uniformToShaders.TryGetValue(variable.Name, out var shaderSet))
                    {
                        shaderSet = new SortedSet<string>(StringComparer.Ordinal);
                        uniformToShaders[variable.Name] = shaderSet;
                    }

                    shaderSet.Add(shaderName);
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Error processing '{fullPath}': {e.Message}");
            }
        }
    }

    if (seenShaders.Count > 0)
    {
        Console.WriteLine($"  Found {seenShaders.Count} unique shaders.");
    }
}

if (featureToShaders.Count == 0)
{
    Console.Error.WriteLine("No shader features found.");
    return;
}

WriteCatalog("feature_to_shaders.txt", featureToShaders);
WriteCatalog("uniform_to_shaders.txt", uniformToShaders);
WriteCatalog("shader_to_games.txt", shaderToGames);

static void WriteCatalog(string fileName, SortedDictionary<string, SortedSet<string>> catalog)
{
    using var writer = new StreamWriter(fileName, append: false, Encoding.UTF8);

    foreach (var (key, shaders) in catalog)
    {
        writer.WriteLine($"{key}: {string.Join(", ", shaders)}");
    }

    Console.WriteLine($"Written {catalog.Count} entries to {fileName}");
}
