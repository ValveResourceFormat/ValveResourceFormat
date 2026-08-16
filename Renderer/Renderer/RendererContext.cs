using Microsoft.Extensions.Logging;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer.Buffers;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// Shared context containing loaders and caches used by the renderer.
/// </summary>
public class RendererContext : IDisposable
{
    /// <summary>
    /// Logger for diagnostic messages.
    /// </summary>
    public ILogger Logger { get; }

    /// <summary>
    /// Game file loader for loading resources from packages.
    /// </summary>
    public GameFileLoader FileLoader { get; }

    /// <summary>
    /// Material and texture loader and cache.
    /// </summary>
    public MaterialLoader MaterialLoader { get; }

    /// <summary>
    /// Shader compiler and cache.
    /// </summary>
    public ShaderLoader ShaderLoader { get; }

    /// <summary>
    /// GPU mesh buffer and vertex array object cache.
    /// </summary>
    public GPUMeshBufferCache MeshBufferCache { get; }

    /// <summary>
    /// Render state tracker for the GL context this renderer context renders with.
    /// </summary>
    public RenderStateTracker RenderState { get; } = new();

    private SceneTextures? sceneTextures;

    /// <summary>
    /// Bindless handles of the scene-wide textures, read by every shader out of one shared buffer.
    /// </summary>
    /// <remarks>
    /// Created on first use rather than with the context, which is constructed before there is a GL context
    /// current to allocate its buffer and null textures on.
    /// </remarks>
    public SceneTextures SceneTextures => sceneTextures ??= new SceneTextures(MaterialLoader);

    /// <summary>
    /// Maximum texture mip size to load in <see cref="MaterialLoader"/>.
    /// </summary>
    public int MaxTextureSize { get; set; } = 1024;

    /// <summary>
    /// Main camera field of view, in horizontal degrees at a 4:3 aspect ratio.
    /// See <see cref="Camera.FieldOfView"/>.
    /// </summary>
    public float FieldOfView { get; set; } = 90.0f;

    /// <summary>
    /// First-person viewmodel field of view, in horizontal degrees at a 4:3 aspect ratio.
    /// </summary>
    public float ViewmodelFieldOfView { get; set; } = 64.0f;

    /// <summary>
    /// Initializes a new renderer context.
    /// </summary>
    /// <param name="fileLoader">Game file loader for resource access.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public RendererContext(GameFileLoader fileLoader, ILogger logger)
    {
        FileLoader = fileLoader;
        Logger = logger;

        MaterialLoader = new MaterialLoader(this);
        ShaderLoader = new ShaderLoader(this);
        MeshBufferCache = new GPUMeshBufferCache(this);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases resources owned by the context.
    /// </summary>
    /// <param name="disposing">True to release managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        ShaderLoader?.Dispose();
    }
}
