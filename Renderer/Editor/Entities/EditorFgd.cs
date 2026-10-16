using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.Fgd;

namespace ValveResourceFormat.Renderer.Editor.Entities;

/// <summary>
/// Gives the entity classes a map is shown with. They are the ones its game declares for Hammer in its game
/// data files. Those come with the tools of a game, so where they are not installed the classes built into
/// the renderer stand in for them: the ones generated from the games whose tools were at hand, and the ones
/// kept by hand for entities no FGD file declares.
/// </summary>
public static class EditorFgd
{
    private const string ResourcePrefix = "Renderer.Resources.";
    private const string GeneratedFileName = "hammer_classes.fgd";
    private const string ExtraFileName = "hammer_classes_extra.fgd";

    private static readonly Lazy<ForgeGameData> BuiltIn = new(LoadBuiltIn);
    private static readonly ConditionalWeakTable<IFileLoader, ForgeGameData> Loaded = [];

    /// <summary>
    /// Gets the entity classes of the game a file loader has found. They are read once for a file loader,
    /// so everything that shows its entities shares them.
    /// </summary>
    /// <param name="fileLoader">The file loader of the map.</param>
    /// <param name="logger">Where it is said which classes are used.</param>
    /// <returns>The classes of the game, or the built in ones where the game has none on disk.</returns>
    public static ForgeGameData Load(IFileLoader fileLoader, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(fileLoader);
        ArgumentNullException.ThrowIfNull(logger);

        return Loaded.GetValue(fileLoader, loader => LoadFromGame(loader, logger));
    }

    private static ForgeGameData LoadFromGame(IFileLoader fileLoader, ILogger logger)
    {
        if (fileLoader is GameFileLoader gameFileLoader && gameFileLoader.LoadFgd() is { } fgd)
        {
            foreach (var error in fgd.Errors)
            {
                logger.LogDebug("{FileName}({Line}): {Message}", error.FileName, error.Line, error.Message);
            }

            return fgd;
        }

        logger.LogInformation("The FGD files of the game were not found, entities are shown with the built in classes");
        return BuiltIn.Value;
    }

    /// <summary>
    /// Loads the classes built into the renderer, with nothing but what entities are drawn with. A class is
    /// looked up in the generated ones first, and in the ones kept by hand where none of those has its name.
    /// </summary>
    /// <returns>The classes.</returns>
    /// <exception cref="InvalidDataException">The built in classes are missing or cannot be read.</exception>
    public static ForgeGameData LoadBuiltIn()
    {
        var generated = LoadResource(GeneratedFileName);
        generated.Fallback = LoadResource(ExtraFileName);

        return generated;
    }

    private static ForgeGameData LoadResource(string fileName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourcePrefix + fileName)
            ?? throw new InvalidDataException($"Missing resource '{fileName}'.");
        using var reader = new StreamReader(stream);

        var fgd = new ForgeGameData();

        if (!fgd.LoadText(reader.ReadToEnd(), fileName))
        {
            throw new InvalidDataException($"'{fileName}' cannot be read: {fgd.Errors[0].Message}");
        }

        fgd.ResolveBaseClasses();
        return fgd;
    }
}
