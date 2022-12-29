using System;
using Sledge.Formats.FileSystem;

namespace VrfFgdParser;

public class FgdFileResolver : IFileResolver
{
    private string directory;

    public FgdFileResolver(string path)
    {
        directory = Path.GetDirectoryName(path)!;
    }

    public Stream OpenFile(string path)
    {
        var parent = Path.GetDirectoryName(directory);
        var paths = new List<string>
        {
            Path.Join(directory, path),
            Path.Join(parent, path),
            Path.Join(parent, "core", path),
        };

        foreach (var fullpath in paths)
        {
            if (File.Exists(fullpath))
            {
                return File.Open(fullpath, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
        }

        Console.WriteLine($"Failed to find '{path}'");

        return Stream.Null;
    }

    public string[] OpenFolder(string path)
    {
        throw new NotImplementedException();
    }
}
