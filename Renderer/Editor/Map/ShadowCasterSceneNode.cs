using ValveResourceFormat.Renderer.SceneNodes;

namespace ValveResourceFormat.Renderer.Editor.Map;

/// <summary>
/// The faces of a map mesh with a tools material that blocks light, which only cast shadows. The faces are shown by a
/// <see cref="ToolsMaterialSceneNode"/>.
/// </summary>
internal sealed class ShadowCasterSceneNode(Scene scene, RenderableMesh renderableMesh) : MeshSceneNode(scene, renderableMesh);
