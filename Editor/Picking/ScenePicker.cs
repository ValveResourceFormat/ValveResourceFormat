using System.Threading;
using ValveResourceFormat.Renderer;

namespace ValveResourceFormat.Editor.Picking;

/// <summary>What a pick is for.</summary>
public enum PickIntent
{
    /// <summary>Select the picked object.</summary>
    Select,

    /// <summary>Open the picked object for viewing.</summary>
    Open,

    /// <summary>Show detailed information about the picked object.</summary>
    Details,
}

/// <summary>Modifier keys held when a pick was requested.</summary>
[Flags]
public enum PickModifiers
{
    /// <summary>No modifier.</summary>
    None = 0,

    /// <summary>Control was held.</summary>
    Control = 1 << 0,

    /// <summary>Shift was held.</summary>
    Shift = 1 << 1,

    /// <summary>Alt was held.</summary>
    Alt = 1 << 2,
}

/// <summary>A request to find what is drawn under a point in the viewport.</summary>
/// <param name="X">X position in viewport pixels.</param>
/// <param name="Y">Y position in viewport pixels, from the top.</param>
/// <param name="Intent">What the pick is for.</param>
/// <param name="Modifiers">Modifier keys held when the pick was requested.</param>
public readonly record struct PickRequest(int X, int Y, PickIntent Intent, PickModifiers Modifiers = PickModifiers.None);

/// <summary>What a <see cref="PickRequest"/> found.</summary>
/// <param name="Request">The request this answers.</param>
/// <param name="Pixel">The node and mesh drawn at the requested point.</param>
public readonly record struct PickResult(PickRequest Request, PickingTexture.PixelInfo Pixel)
{
    /// <summary>Gets the intent of the request.</summary>
    public PickIntent Intent => Request.Intent;

    /// <summary>Gets whether nothing is drawn at the requested point.</summary>
    public bool HitNothing => Pixel.ObjectId == 0;

    /// <summary>Gets whether the hit belongs to the 3D skybox scene rather than the main scene.</summary>
    public bool IsInSkybox => Pixel.IsSkybox != 0;

    /// <summary>Gets whether the given modifier keys were all held when the pick was requested.</summary>
    /// <param name="modifiers">The modifiers to check.</param>
    /// <returns><see langword="true"/> when every one of <paramref name="modifiers"/> was held.</returns>
    public bool Has(PickModifiers modifiers) => (Request.Modifiers & modifiers) == modifiers;
}

/// <summary>
/// Turns clicks into picks. Requests can come from any thread and are queued; the render thread draws
/// the object id pass once for all of them, and hands each result to the viewer at the end of the frame.
/// </summary>
public sealed class ScenePicker : IDisposable
{
    private readonly Lock requestLock = new();
    private readonly List<PickRequest> pendingRequests = [];
    private readonly List<PickRequest> frameRequests = [];
    private readonly List<PickResult> results = [];
    private readonly Action<PickResult> onPicked;

    /// <summary>Gets the framebuffer the object id pass is drawn into.</summary>
    public PickingTexture Texture { get; }

    /// <summary>Gets or sets whether picking is available. Requests made while disabled are dropped.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets whether a request is waiting for the next frame.</summary>
    public bool HasPendingRequests
    {
        get
        {
            using var _ = requestLock.EnterScope();
            return pendingRequests.Count > 0;
        }
    }

    /// <summary>Creates the picking framebuffer.</summary>
    /// <param name="rendererContext">Renderer context for loading shaders.</param>
    /// <param name="onPicked">Called on the render thread with each result.</param>
    public ScenePicker(RendererContext rendererContext, Action<PickResult> onPicked)
    {
        Texture = new PickingTexture(rendererContext);
        this.onPicked = onPicked;
    }

    /// <summary>Queues a pick to be resolved on the next frame.</summary>
    /// <param name="request">The point to pick and what for.</param>
    public void Request(PickRequest request)
    {
        if (!Enabled)
        {
            return;
        }

        using var _ = requestLock.EnterScope();
        pendingRequests.Add(request);
    }

    /// <summary>Resizes the picking framebuffer to the viewport.</summary>
    /// <param name="width">Viewport width in pixels.</param>
    /// <param name="height">Viewport height in pixels.</param>
    public void Resize(int width, int height) => Texture.Resize(width, height);

    /// <summary>
    /// Draws the object id pass if any pick is pending, and reads back every pending point.
    /// </summary>
    /// <param name="renderer">The renderer that draws the scenes.</param>
    /// <param name="renderContext">The frame's render context, which the pass replaces the shader and target of.</param>
    public void Render(Renderer.Renderer renderer, Scene.RenderContext renderContext)
    {
        using (requestLock.EnterScope())
        {
            frameRequests.AddRange(pendingRequests);
            pendingRequests.Clear();
        }

        if (frameRequests.Count == 0)
        {
            return;
        }

        using (new GLDebugGroup("Picker Object Id Render"))
        {
            renderer.RenderScenesWithView(renderContext with { ReplacementShader = Texture.Shader, Framebuffer = Texture });
        }

        foreach (var request in frameRequests)
        {
            results.Add(new PickResult(request, Texture.ReadPixel(request.X, request.Y)));
        }

        frameRequests.Clear();
    }

    /// <summary>Hands the results read back by <see cref="Render"/> to the viewer, in the order they were requested.</summary>
    public void DispatchResults()
    {
        if (results.Count == 0)
        {
            return;
        }

        foreach (var result in results)
        {
            onPicked(result);
        }

        results.Clear();
    }

    /// <summary>Deletes the picking framebuffer.</summary>
    public void Dispose()
    {
        Texture.Delete();
    }
}
