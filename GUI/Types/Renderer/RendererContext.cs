using ValveResourceFormat.IO;

namespace GUI.Types.Renderer;

public class RendererContext : IDisposable
{
    public GameFileLoader FileLoader { get; private set; }

    public MaterialLoader MaterialLoader { get; }
    public ShaderLoader ShaderLoader { get; }
    public GPUMeshBufferCache MeshBufferCache { get; }

    public RendererContext(GameFileLoader fileLoader)
    {
        FileLoader = fileLoader;

        MaterialLoader = new MaterialLoader(this);
        ShaderLoader = new ShaderLoader(this);
        MeshBufferCache = new GPUMeshBufferCache();
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
