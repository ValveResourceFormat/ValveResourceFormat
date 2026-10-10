using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Editor.Entities;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Materials;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Editor.Selection;

/// <summary>Which lighting a selected node uses is shown linked to it.</summary>
public enum LightingBindingDisplay
{
    /// <summary>None.</summary>
    None,

    /// <summary>The environment maps the node is shaded with.</summary>
    EnvMaps,

    /// <summary>The light probe volume the node is bound to.</summary>
    LightProbe,
}

/// <summary>
/// Draws the helpers for a <see cref="SelectionSet"/> over the final image, so their colors show as given:
/// bounds with edge lengths, the shapes a selected entity's class declares, the node's name, and optionally
/// the lighting it uses. Lines and faces are solid where the scene does not hide them, and faint over it.
/// The outline itself is drawn by the renderer for every node with <see cref="SceneNode.IsSelected"/>.
/// </summary>
public sealed class SelectionVisuals
{
    private static readonly Vector2 NodeNameOffset = new(0, -20);
    private static readonly Color32 LightingBindingColor = new(1.0f, 0.0f, 1.0f, 1.0f);

    private readonly SelectionSet selection;
    private readonly HelperVertices vertices = new();
    private readonly List<SceneEnvMap> envMaps = [];

    private readonly LineBuffer lines;
    private readonly LineBuffer overlayLines;
    private readonly LineBuffer faces;
    private readonly LineBuffer overlayFaces;

    /// <summary>Gets or sets which lighting is shown linked to each selected node.</summary>
    public LightingBindingDisplay LightingBindings { get; set; }

    /// <summary>Creates the buffers the helpers are drawn with.</summary>
    /// <param name="rendererContext">Renderer context for loading shaders.</param>
    /// <param name="selection">The selection to draw.</param>
    public SelectionVisuals(RendererContext rendererContext, SelectionSet selection)
    {
        const string Shader = "helper_lines";

        this.selection = selection;

        lines = new LineBuffer(rendererContext, nameof(SelectionVisuals), Shader);
        overlayLines = new LineBuffer(rendererContext, nameof(SelectionVisuals) + " overlay", Shader);
        faces = new LineBuffer(rendererContext, nameof(SelectionVisuals) + " faces", Shader);
        overlayFaces = new LineBuffer(rendererContext, nameof(SelectionVisuals) + " overlay faces", Shader);
    }

    /// <summary>Rebuilds the helper shapes and labels for the current selection.</summary>
    /// <param name="renderContext">Render context providing the camera.</param>
    /// <param name="updateContext">Update context providing the text renderer.</param>
    public void Update(Scene.RenderContext renderContext, Scene.UpdateContext updateContext)
    {
        var nodes = selection.Nodes;

        if (nodes.Count == 0)
        {
            // We don't need to reupload an empty array
            ClearBuffers();
            return;
        }

        foreach (var node in nodes)
        {
            AddNodeHelpers(renderContext.Camera, updateContext.TextRenderer, node);
        }

        lines.Upload(vertices.Lines);
        overlayLines.Upload(vertices.OverlayLines);
        faces.Upload(vertices.Faces);
        overlayFaces.Upload(vertices.OverlayFaces);

        vertices.Clear();
    }

    /// <summary>Draws the helpers built by the last <see cref="Update"/> over the final image, after post processing.</summary>
    /// <param name="sceneDepth">The scene's resolved depth, to find what it hides.</param>
    public void Render(RenderTexture sceneDepth)
    {
        if (lines.VertexCount == 0 && overlayLines.VertexCount == 0)
        {
            return;
        }

        var shader = lines.Shader;
        shader.Use();
        shader.SetTexture((int)ReservedTextureSlots.SceneDepth, "g_tSceneDepth", sceneDepth);

        using var _ = GraphicsContext.RenderState.Scope(depthTest: false, depthWrite: false, blend: true);

        shader.SetUniform("g_bDepthTest", false);
        overlayFaces.Draw(0, overlayFaces.VertexCount, primitive: PrimitiveType.Triangles);

        // Faces pull further towards the camera, so they still show where they lie on the scene's surfaces
        shader.SetUniform("g_bDepthTest", true);
        shader.SetUniform("g_flDepthTolerance", 0.004f);
        faces.Draw(0, faces.VertexCount, primitive: PrimitiveType.Triangles);

        shader.SetUniform("g_flDepthTolerance", 0.001f);
        lines.Draw();

        shader.SetUniform("g_bDepthTest", false);
        overlayLines.Draw();
    }

    /// <summary>Deletes the GL objects.</summary>
    public void Delete()
    {
        lines.Delete();
        overlayLines.Delete();
        faces.Delete();
        overlayFaces.Delete();
    }

    private void ClearBuffers()
    {
        lines.Clear();
        overlayLines.Clear();
        faces.Clear();
        overlayFaces.Clear();
    }

    private void AddNodeHelpers(Camera camera, TextRenderer textRenderer, SceneNode node)
    {
        var nodeName = node.Name ?? node.GetType().Name;

        // Drawn with the main camera, so 3D sky nodes are outlined where they appear in the world
        var toWorld = node.Scene.ToViewerWorld;
        var bounds = node.BoundingBox.Transform(toWorld);
        var hasHelpers = false;

        if (node.EntityData != null)
        {
            var classname = node.EntityData.GetStringProperty("classname");
            if (classname != null)
            {
                nodeName = classname;
            }

            var placement = node.EntityInstance?.RigidTransform ?? (node.IsPointMarker
                ? EntityTransformHelper.ToRigidTransformationMatrix(node.EntityData)
                : node.Transform);

            hasHelpers = EntityHelperLines.TryAdd(vertices, classname, node.EntityData, node.EntityInstance, placement, toWorld);
        }

        // The helpers show the entity better than the bounds of its editor model
        if (!(hasHelpers && node.LayerName == HammerEntityVisuals.MarkerLayerName) && !node.IsPointMarker)
        {
            BoxLines.AddWithSize(vertices, node.Transform * toWorld, node.LocalBoundingBox, Color32.White, camera, textRenderer);
        }

        AddLightingBindings(node, toWorld, bounds.Center);

        // draw node name above the bounding box
        var position = bounds.Center;
        position.Z = bounds.Max.Z;

        textRenderer.AddTextBillboard(position, new TextRenderer.TextRenderRequest
        {
            Scale = 20f,
            Text = nodeName,
            CenterHorizontal = true,
            TextOffset = NodeNameOffset
        }, camera, fixedScale: false);
    }

    private void AddLightingBindings(SceneNode node, in Matrix4x4 toWorld, Vector3 nodeCenter)
    {
        switch (LightingBindings)
        {
            case LightingBindingDisplay.EnvMaps:
                envMaps.Clear();
                node.Scene.GetEnvMapsUsedBy(node, envMaps);

                foreach (var envMap in envMaps)
                {
                    AddLink(envMap, toWorld, nodeCenter);
                }

                break;

            case LightingBindingDisplay.LightProbe when node.LightProbeBinding is { } probe:
                AddLink(probe, toWorld, nodeCenter);
                break;
        }
    }

    /// <summary>Outlines a lighting volume and connects it to the node that uses it.</summary>
    private void AddLink(SceneNode source, in Matrix4x4 toWorld, Vector3 nodeCenter)
    {
        var transform = source.Transform * toWorld;

        BoxLines.Add(vertices, transform, source.LocalBoundingBox, LightingBindingColor);
        BoxLines.AddLine(vertices, transform.Translation, nodeCenter, LightingBindingColor);
    }
}
