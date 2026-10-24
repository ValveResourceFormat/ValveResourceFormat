using ValveKeyValue;

namespace ValveResourceFormat.IO.Fgd;

public sealed partial class ForgeGameData
{
    /// <summary>
    /// Loads the files the <c>Hammer</c> block of the <c>gameinfo.gi</c> of a game names, the way Hammer does
    /// when it starts: the one under <c>fgd</c>, and the ones under <c>fgd_files</c> that exist, as a game can
    /// name some it does not ship. Base classes are resolved after each file.
    /// </summary>
    /// <param name="hammer">The <c>Hammer</c> block.</param>
    /// <returns>Whether any of the files exists. They come with the tools of a game.</returns>
    public bool LoadGameFiles(KVObject hammer)
    {
        ArgumentNullException.ThrowIfNull(hammer);

        var files = new List<string>();

        if (hammer.TryGetValue("fgd", out var single) && single?.ToString() is { Length: > 0 } fileName)
        {
            files.Add(fileName);
        }

        if (hammer.TryGetValue("fgd_files", out var list) && list != null)
        {
            foreach (var (name, _) in list)
            {
                if (Exists(name))
                {
                    files.Add(name);
                }
            }
        }

        var found = false;

        foreach (var file in files)
        {
            found |= Exists(file);

            Load(file);

            // Every class is resolved again after each file, so what a later file adds to a base class
            // reaches the classes based on it
            ResolveBaseClasses();
        }

        return found;
    }
}
