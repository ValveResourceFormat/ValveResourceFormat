using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.IO.Fgd;

/// <summary>
/// Gives the entity classes a map is shown with, from the tables built into the library. Each game whose
/// classes are known has a table of its own, found by its <see cref="GameInfo.Game"/>. Any other game is
/// shown with the classes of every known game, each the version most of them declare, and the ones kept by
/// hand for entities none of them declares.
/// </summary>
public static partial class EntityClassCatalog
{
    private const string ResourcePrefix = "ValveResourceFormat.IO.Fgd.";
    private const string GeneratedFileName = "entity_classes.kv3";
    private const string ExtraFileName = "entity_classes_extra.kv3";

    private static readonly Lazy<BuiltInTables> BuiltIn = new(LoadBuiltIn);
    private static readonly ConditionalWeakTable<IFileLoader, EntityClassTable> Loaded = [];

    /// <summary>
    /// Gets the entity classes of the game a file loader has found. They are found once for a file loader, so
    /// everything that shows its entities shares them.
    /// </summary>
    /// <param name="fileLoader">The file loader of the map.</param>
    /// <param name="logger">Where it is said which classes are used.</param>
    /// <returns>The classes of the game, or the ones of every game where it is not one whose classes are known.</returns>
    public static EntityClassTable Get(IFileLoader fileLoader, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(fileLoader);
        ArgumentNullException.ThrowIfNull(logger);

        return Loaded.GetValue(fileLoader, loader => FindTable(loader, logger));
    }

    /// <summary>Gets the entity classes of a game.</summary>
    /// <param name="game">The game, or <see cref="Game.Unknown"/> where it is not known.</param>
    /// <returns>The classes of the game, or the ones of every game where it is not one whose classes are known.</returns>
    /// <exception cref="InvalidDataException">The built in classes are missing or cannot be read.</exception>
    public static EntityClassTable GetTable(Game game)
    {
        var tables = BuiltIn.Value;

        return tables.Games.GetValueOrDefault(game) ?? tables.Merged;
    }

    /// <summary>Gets the entity classes of the game a file loader has found.</summary>
    /// <param name="fileLoader">The file loader of the map.</param>
    /// <returns>The classes of the game, or the ones of every game where it is not one whose classes are known.</returns>
    /// <exception cref="InvalidDataException">The built in classes are missing or cannot be read.</exception>
    public static EntityClassTable GetTable(IFileLoader fileLoader)
    {
        ArgumentNullException.ThrowIfNull(fileLoader);

        return GetTable(fileLoader.GameInfo.Game);
    }

    private static EntityClassTable FindTable(IFileLoader fileLoader, ILogger logger)
    {
        if (fileLoader is GameFileLoader { ResourceProvider: { } provider } && provider.GetEntityClasses(logger) is { } providerTable)
        {
            return providerTable;
        }

        var game = fileLoader.GameInfo.Game;

        if (BuiltIn.Value.Games.TryGetValue(game, out var table))
        {
            logger.LogInformation("Entities are shown with the {Count} classes of {Game}", table.Classes.Count, game);
            return table;
        }

        logger.LogInformation("The entity classes of the game are not known, entities are shown with the classes of every game");
        return BuiltIn.Value.Merged;
    }

    private sealed record BuiltInTables(Dictionary<Game, EntityClassTable> Games, EntityClassTable Merged);

    private static BuiltInTables LoadBuiltIn()
    {
        var assembly = typeof(EntityClassCatalog).Assembly;
        var generated = EntityClassReader.ReadResource(assembly, ResourcePrefix + GeneratedFileName);
        var extra = EntityClassReader.ReadResource(assembly, ResourcePrefix + ExtraFileName);

        var games = new Dictionary<Game, EntityClassTable>();
        var gameClasses = new List<EntityClass>();
        (int, int, int, int)? mergedGridNav = null;

        foreach (var game in EntityClassReader.ReadTables(generated))
        {
            var detectedGame = GameInfo.GetGame(game.AppId);

            if (detectedGame != Game.Unknown)
            {
                // Tables are read by app id, so of apps that are one game the lowest one is its table
                games.TryAdd(detectedGame, game.Table);
            }

            // Every game adds its classes to the merged table, including a game that is not detected and an app that is not the table of its game
            gameClasses.AddRange(game.Table.Classes);
            mergedGridNav ??= game.Table.GridNav;
        }

        var extraClasses = (extra.GetArray("classes") ?? []).Select(EntityClassReader.ReadClass);
        var merged = new EntityClassTable(EntityClassReader.MergeClasses(gameClasses).Concat(extraClasses), mergedGridNav);

        return new BuiltInTables(games, merged);
    }
}
