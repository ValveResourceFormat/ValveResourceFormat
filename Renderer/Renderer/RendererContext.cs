using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Source1.FileSystem;
using ValveResourceFormat.Source1.Particles;

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
    /// File system of the Source 1 game that <c>.vmt</c> materials and <c>.vtf</c> textures are read from.
    /// </summary>
    internal Source1FileSystem? Source1FileSystem { get; set; }

    /// <summary>
    /// Whether Source 1 materials are loaded in high dynamic range, which selects the <c>hdr</c> variants of their
    /// conditional blocks and the <c>.hdr.vtf</c> variants of their cube maps. Maps without high dynamic range lighting turn it off.
    /// It has to be set before the materials of the map are loaded.
    /// </summary>
    internal bool Source1HighDynamicRange { get; set; } = true;

    private Source1ParticleLibrary? source1Particles;

    /// <summary>
    /// The particle systems of the Source 1 game, or <see langword="null"/> without a <see cref="Source1FileSystem"/>.
    /// </summary>
    internal Source1ParticleLibrary? Source1Particles
    {
        get
        {
            if (source1Particles == null && Source1FileSystem != null)
            {
                source1Particles = new Source1ParticleLibrary(Source1FileSystem, Logger);
            }

            return source1Particles;
        }
    }

    /// <summary>
    /// Loads a particle system by name from the Source 1 game when it has one of that name, and from the game files otherwise.
    /// </summary>
    /// <param name="name">The name of the particle system.</param>
    /// <returns>The particle system, or <see langword="null"/> when there is none.</returns>
    internal ParticleSystem? LoadParticleSystem(string name)
        => Source1Particles?.GetParticleSystem(name) ?? FileLoader.LoadFileCompiled(name)?.DataBlock as ParticleSystem;

    /// <summary>
    /// Information lookup from compiled shaders (.vcs).
    /// </summary>
    public ShaderDataProvider ShaderDataProvider { get; }

    /// <summary>
    /// Owns the GPU objects created for this renderer. Creation goes through the static methods on
    /// <see cref="GraphicsDevice"/> rather than through this property.
    /// </summary>
    public GraphicsDevice Device { get; }

    /// <summary>
    /// Material and texture loader and cache.
    /// </summary>
    public MaterialLoader MaterialLoader { get; }

    /// <summary>
    /// Background loader for textures.
    /// </summary>
    public TextureStreamingHelper TextureStreaming { get; }

    /// <summary>
    /// Shader compiler and cache.
    /// </summary>
    public ShaderLoader ShaderLoader { get; }

    /// <summary>
    /// GPU mesh buffer and vertex array object cache.
    /// </summary>
    public GPUMeshBufferCache MeshBufferCache { get; }

    private bool disposed;

    /// <summary>
    /// Maximum texture mip size to load in <see cref="MaterialLoader"/>.
    /// </summary>
    public int MaxTextureSize { get; set; } = int.MaxValue;

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
    /// Holds the morph composites of every mesh loaded through this context.
    /// </summary>
    public MorphCompositeAtlas MorphAtlas { get; }

    /// <summary>
    /// Holds the sheet sequences of every texture the particle renderers of this context draw.
    /// </summary>
    internal Particles.ParticleSheetCache ParticleSheets { get; } = new();

    internal ConcurrentDictionary<string, Lazy<List<AnimationBase>>> ModelAnimations { get; } = new();

    /// <summary>
    /// Whether scene nodes simulate across the thread pool, published by the renderer each frame.
    /// </summary>
    public bool ParallelSimulation { get; set; } = true;

    /// <summary>
    /// Initializes a new renderer context.
    /// </summary>
    /// <param name="fileLoader">Game file loader for resource access.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public RendererContext(GameFileLoader fileLoader, ILogger logger)
    {
        FileLoader = fileLoader;
        ShaderDataProvider = new ShaderDataProvider(fileLoader);
        Logger = logger;
        Device = GraphicsDevice.Create();

        TextureStreaming = new TextureStreamingHelper(this);
        MaterialLoader = new MaterialLoader(this);
        ShaderLoader = new ShaderLoader(this);
        MeshBufferCache = new GPUMeshBufferCache(this);
        MorphAtlas = new MorphCompositeAtlas(this);
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
        if (!disposing || disposed)
        {
            return;
        }

        CancelLoading();

        disposed = true;

        TextureStreaming.CancelAllStreaming();

        ShaderLoader?.Dispose();
        MorphAtlas.Dispose();
    }

    // Outlive Dispose: the loader wait runs off the UI thread after teardown and reads these fields.
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Outlives Dispose so WaitForLoadingToStop still works after it")]
    private readonly CancellationTokenSource loadCancellation = new();

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Outlives Dispose so WaitForLoadingToStop still works after it")]
    private readonly ManualResetEventSlim loadIdle = new(true);
    private int loadsInFlight;

    private static readonly TimeSpan LoadStopTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Cancels loading on this context.</summary>
    public CancellationToken CancellationToken => loadCancellation.Token;

    /// <summary>Marks loading in progress until the returned scope is disposed.</summary>
    public IDisposable BeginLoading()
    {
        if (Interlocked.Increment(ref loadsInFlight) == 1)
        {
            loadIdle.Reset();
        }

        return new LoadScope(this);
    }

    /// <summary>Asks loading to stop. Returns straight away without waiting for it.</summary>
    public void CancelLoading()
    {
        if (!disposed)
        {
            loadCancellation.Cancel();
        }
    }

    /// <summary>Waits for everything reading resources to stop, returning whether it did.</summary>
    public bool WaitForLoadingToStop()
    {
        var stopped = loadIdle.Wait(LoadStopTimeout);

        if (!stopped)
        {
            Logger.LogWarning("Loading did not stop within {Timeout}, carrying on without it", LoadStopTimeout);
        }

        TextureStreaming.DrainPendingLoads();

        return stopped;
    }

    private sealed class LoadScope(RendererContext context) : IDisposable
    {
        public void Dispose()
        {
            if (Interlocked.Decrement(ref context.loadsInFlight) == 0)
            {
                context.loadIdle.Set();
            }
        }
    }
}
