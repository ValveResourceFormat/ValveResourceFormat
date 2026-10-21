namespace ValveResourceFormat.Renderer;

/// <summary>
/// What the caller wants rendered this frame. The renderer derives the views it draws from it.
/// </summary>
public struct ViewContext
{
    /// <summary>Gets or sets the camera the main view is drawn through.</summary>
    public required Camera Camera { get; set; }
}
