using ValveResourceFormat;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.IO;

namespace VmdlExtractor;

internal sealed class FolderFileLoader : IFileLoader, IDisposable
{
    private readonly string _baseDir;
    private readonly Dictionary<string, Resource?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public FolderFileLoader(string baseDir)
    {
        _baseDir = Path.GetFullPath(baseDir);
    }

    public Resource? LoadFile(string file)
    {
        if (_cache.TryGetValue(file, out var cached)) return cached;

        var fullPath = ResolveOnDisk(file);
        if (fullPath == null) { _cache[file] = null; return null; }

        try
        {
            var res = new Resource { FileName = file };
            res.Read(fullPath);
            _cache[file] = res;
            return res;
        }
        catch
        {
            _cache[file] = null;
            return null;
        }
    }

    public Resource? LoadFileCompiled(string file) => LoadFile(file + "_c");

    public ShaderCollection? LoadShader(string shaderName) => null;

    public byte[]? TryReadRawCompiled(string file)
    {
        var fullPath = ResolveOnDisk(file);
        return fullPath == null ? null : File.ReadAllBytes(fullPath);
    }

    public Stream? GetFileStream(string file)
    {
        var fullPath = ResolveOnDisk(file);
        return fullPath == null ? null : File.OpenRead(fullPath);
    }

    private string? ResolveOnDisk(string file)
    {
        var fullPath = Path.Combine(_baseDir, file.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(fullPath) ? fullPath : null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var r in _cache.Values) r?.Dispose();
        _cache.Clear();
    }
}
