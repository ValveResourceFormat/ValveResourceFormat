using Microsoft.Extensions.Logging;
using ValveResourceFormat.IO;

namespace ValveResourceFormat.Renderer;

public class RendererContext : IDisposable
{
    public ILogger Logger { get; }
    public GameFileLoader FileLoader { get; }
    public MaterialLoader MaterialLoader { get; }
    public ShaderLoader ShaderLoader { get; }
    public GPUMeshBufferCache MeshBufferCache { get; }

    /// <summary>
    /// Maximum texture mip size to load in <see cref="MaterialLoader"/>.
    /// </summary>
    public int MaxTextureSize { get; set; } = 1024;

    /// <summary>
    /// Field of view in degrees for <see cref="Camera"/>
    /// </summary>
    public float FieldOfView { get; set; } = 60.0f;

    public RendererContext(GameFileLoader fileLoader, ILogger logger)
    {
        FileLoader = fileLoader;
        Logger = logger;

        MaterialLoader = new MaterialLoader(this);
        ShaderLoader = new ShaderLoader(this);
        MeshBufferCache = new GPUMeshBufferCache(this);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        ShaderLoader?.Dispose();
    }
}
