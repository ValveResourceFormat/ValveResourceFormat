using System.IO;

namespace ValveResourceFormat.Source1.FileSystem;

/// <summary>
/// Keeps one value for each Source 1 game, such as its file system, so every file of the game shares it.
/// </summary>
/// <typeparam name="T">The kind of value. The cache disposes the values with itself.</typeparam>
internal sealed class Source1GameCache<T> : IDisposable where T : class, IDisposable
{
    private readonly Dictionary<string, T?> values = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the value of the game that a file belongs to, creating it on the first request.
    /// </summary>
    /// <param name="diskPath">The path on disk of the file, or of the package it is in.</param>
    /// <param name="create">Creates the value of the game, or returns <see langword="null"/> when the game is not known.</param>
    /// <returns>The value, or <see langword="null"/> when the game is not known.</returns>
    public T? GetOrCreate(string diskPath, Func<T?> create)
    {
        var key = Source1FileSystem.FindGameInfo(diskPath) ?? Path.GetDirectoryName(diskPath) ?? diskPath;

        lock (values)
        {
            if (!values.TryGetValue(key, out var value))
            {
                value = create();
                values.Add(key, value);
            }

            return value;
        }
    }

    public void Dispose()
    {
        lock (values)
        {
            foreach (var value in values.Values)
            {
                value?.Dispose();
            }

            values.Clear();
        }
    }
}
