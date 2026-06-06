using System.IO;
using ValveKeyValue;

namespace ValveResourceFormat.DemoPlayback;

/// <summary>
/// Parses CS2 agent definitions from items_game.txt for def-index and name lookups.
/// </summary>
public sealed class CsDemoAgentCatalog
{
    private static readonly string[] ItemsGamePaths =
    [
        "scripts/items/items_game.txt",
        "scripts/items/items_game_cdn.txt",
    ];

    private readonly CsDemoFileAccess fileAccess;
    private readonly Dictionary<ushort, string> modelByDefIndex = [];
    private readonly Dictionary<string, string> modelByName = new(StringComparer.OrdinalIgnoreCase);
    private bool loaded;

    public CsDemoAgentCatalog(CsDemoFileAccess fileAccess)
    {
        this.fileAccess = fileAccess;
    }

    public bool TryGetModelByDefIndex(ushort defIndex, out string modelPath)
    {
        EnsureLoaded();
        return modelByDefIndex.TryGetValue(defIndex, out modelPath!);
    }

    public bool TryGetModelByName(string? name, out string modelPath)
    {
        EnsureLoaded();

        if (string.IsNullOrWhiteSpace(name))
        {
            modelPath = string.Empty;
            return false;
        }

        if (modelByName.TryGetValue(name, out modelPath!))
        {
            return true;
        }

        if (name.StartsWith("customplayer_", StringComparison.OrdinalIgnoreCase)
            && modelByName.TryGetValue(name["customplayer_".Length..], out modelPath!))
        {
            return true;
        }

        modelPath = string.Empty;
        return false;
    }

    private void EnsureLoaded()
    {
        if (loaded)
        {
            return;
        }

        loaded = true;

        foreach (var path in ItemsGamePaths)
        {
            if (TryLoadFromPath(path))
            {
                break;
            }
        }
    }

    private bool TryLoadFromPath(string path)
    {
        using var stream = fileAccess.OpenTextFile(path);

        if (stream == null)
        {
            return false;
        }

        try
        {
            var root = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream);
            var items = root["items"];

            if (items == null)
            {
                return false;
            }

            foreach (var (defIndexText, itemObject) in items)
            {
                if (itemObject is not KVObject item)
                {
                    continue;
                }

                var modelPlayer = item["model_player"]?.ToString();
                if (string.IsNullOrWhiteSpace(modelPlayer))
                {
                    continue;
                }

                modelPlayer = NormalizeModelPath(modelPlayer);

                if (!ushort.TryParse(defIndexText, out var defIndex))
                {
                    continue;
                }

                modelByDefIndex[defIndex] = modelPlayer;

                var itemName = item["name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(itemName))
                {
                    modelByName[itemName] = modelPlayer;

                    if (itemName.StartsWith("customplayer_", StringComparison.OrdinalIgnoreCase))
                    {
                        modelByName[itemName["customplayer_".Length..]] = modelPlayer;
                    }
                }
            }

            return modelByDefIndex.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeModelPath(string modelPath)
    {
        modelPath = modelPath.Replace('\\', '/').Trim();

        if (modelPath.EndsWith("_c", StringComparison.OrdinalIgnoreCase))
        {
            modelPath = modelPath[..^2];
        }

        if (modelPath.EndsWith(".vmdl_c", StringComparison.OrdinalIgnoreCase))
        {
            modelPath = modelPath[..^2];
        }

        return modelPath;
    }
}
