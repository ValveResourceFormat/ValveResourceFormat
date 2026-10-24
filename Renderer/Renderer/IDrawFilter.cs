using ValveResourceFormat.Renderer.Materials;

namespace ValveResourceFormat.Renderer;

/// <summary>How a node draws one of its materials.</summary>
public enum DrawVisibility
{
    /// <summary>Drawn as the material says.</summary>
    Drawn,

    /// <summary>Not drawn, and casts no shadows.</summary>
    Hidden,

    /// <summary>Only drawn into shadow maps.</summary>
    ShadowOnly,
}

/// <summary>
/// Decides which materials of which nodes are drawn, such as a tool hiding tools materials. The renderer draws
/// every material as it says, and leaves the rest to whoever sets <see cref="RenderSettings.DrawFilter"/>.
/// </summary>
public interface IDrawFilter
{
    /// <summary>Gets how a node draws one of its materials.</summary>
    /// <param name="node">The node.</param>
    /// <param name="material">The material of one of its draws.</param>
    /// <returns>How the draw is drawn.</returns>
    DrawVisibility GetVisibility(SceneNode node, RenderMaterial material);
}
