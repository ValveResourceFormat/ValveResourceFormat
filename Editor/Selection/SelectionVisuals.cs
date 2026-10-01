using System.Globalization;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Materials;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Editor.Selection;

/// <summary>
/// Draws the helpers for a <see cref="SelectionSet"/> over the final image, so their colors show as given:
/// bounds with edge lengths, the shapes a selected entity's class declares, the node's name, and the cubemaps
/// and light probe it uses in the matching render modes. Lines and faces are solid where the scene does not
/// hide them, and faint over it. The outline itself is drawn by the renderer for every node with
/// <see cref="SceneNode.IsSelected"/>.
/// </summary>
public sealed class SelectionVisuals
{
    private static readonly Vector2 NodeNameOffset = new(0, -20);

    // Opacity of the bounds over the scene, out of 255
    private const byte HiddenLineAlpha = 32;

    private readonly SelectionSet selection;
    private readonly HelperVertices vertices = new();
    private readonly HashSet<SceneLightProbe> probesWithGrid = [];
    private readonly HashSet<SceneLightProbe> probesInUse = [];

    private readonly LineBuffer lines;
    private readonly LineBuffer overlayLines;
    private readonly LineBuffer faces;
    private readonly LineBuffer overlayFaces;

    private bool debugCubeMaps;
    private bool debugLightProbes;

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
    /// <param name="renderContext">Render context providing camera and scene state.</param>
    /// <param name="updateContext">Update context providing the text renderer.</param>
    public void Update(Scene.RenderContext renderContext, Scene.UpdateContext updateContext)
    {
        var nodes = selection.Nodes;

        UpdateLightProbeGrids(nodes);

        if (nodes.Count == 0)
        {
            // We don't need to reupload an empty array
            ClearBuffers();
            return;
        }

        foreach (var node in nodes)
        {
            AddNodeHelpers(renderContext, updateContext.TextRenderer, node);
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

    /// <summary>Updates which debug helpers (cubemaps, light probes) are drawn for the active render mode.</summary>
    /// <param name="mode">The render mode name from the viewer.</param>
    public void SetRenderMode(string mode)
    {
        debugCubeMaps = mode == "Cubemaps";
        debugLightProbes = mode is "Irradiance" or "Illumination";
    }

    /// <summary>Removes the light probe grids this drew and deletes the GL objects.</summary>
    public void Delete()
    {
        foreach (var probe in probesWithGrid)
        {
            probe.RemoveDebugGridSpheres();
        }

        probesWithGrid.Clear();

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

    // The grids are scene nodes, so they are added and removed here on the render thread, by comparing
    // against what was shown last frame, rather than whenever the selection changes.
    private void UpdateLightProbeGrids(IReadOnlyList<SceneNode> nodes)
    {
        probesInUse.Clear();

        if (debugLightProbes)
        {
            foreach (var node in nodes)
            {
                if (node.LightProbeBinding is { } probe)
                {
                    probesInUse.Add(probe);
                }
            }
        }

        foreach (var probe in probesWithGrid)
        {
            if (!probesInUse.Contains(probe))
            {
                probe.RemoveDebugGridSpheres();
            }
        }

        foreach (var probe in probesInUse)
        {
            probe.CreateDebugGridSpheres();
        }

        probesWithGrid.Clear();
        probesWithGrid.UnionWith(probesInUse);
    }

    private void AddNodeHelpers(Scene.RenderContext renderContext, TextRenderer textRenderer, SceneNode node)
    {
        var camera = renderContext.Camera;
        var nodeName = node.Name ?? node.GetType().Name;

        // Drawn with the main camera, so 3D sky nodes are outlined where they appear in the world
        var toWorld = node.Scene.ToViewerWorld;
        var bounds = node.BoundingBox.Transform(toWorld);
        var hasHelpers = false;

        if (debugCubeMaps)
        {
            AddCubemapLinks(renderContext, textRenderer, node, toWorld, bounds);
        }

        if (debugLightProbes && node.LightProbeBinding is { } probe)
        {
            var probeTransform = probe.Transform * toWorld;

            AddBox(camera, textRenderer, probeTransform, probe.LocalBoundingBox, new(1.0f, 0.0f, 1.0f, 1.0f));
            AddLine(probeTransform.Translation, bounds.Center, new(1.0f, 0.0f, 1.0f, 1.0f));
        }

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
        if (!(hasHelpers && node.LayerName == EditorEntityNode.LayerName) && !node.IsPointMarker)
        {
            AddBox(camera, textRenderer, node.Transform * toWorld, node.LocalBoundingBox, Color32.White, showSize: true);
        }

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

    private void AddCubemapLinks(Scene.RenderContext renderContext, TextRenderer textRenderer, SceneNode node, in Matrix4x4 toWorld, in AABB bounds)
    {
        IEnumerable<SceneEnvMap> tiedEnvmaps = node.EnvMaps;
        if (renderContext.Scene.LightingInfo.CubemapType == CubemapType.CubemapArray)
        {
            var list = new List<SceneEnvMap>();
            foreach (var shaderId in node.ShaderEnvMapVisibility.GetVisibleShaderIndices())
            {
                var env = renderContext.Scene.LightingInfo.EnvMaps.FirstOrDefault(e => e.ShaderIndex == shaderId);
                if (env is SceneEnvMap sem)
                {
                    list.Add(sem);
                }
            }
            tiedEnvmaps = list;
        }

        var i = 0;

        foreach (var tiedEnvMap in tiedEnvmaps)
        {
            var envMapTransform = tiedEnvMap.Transform * toWorld;

            AddBox(renderContext.Camera, textRenderer, envMapTransform, tiedEnvMap.LocalBoundingBox, new(0.7f, 0.0f, 1.0f, 1.0f));

            if (renderContext.Scene.LightingInfo.CubemapType is CubemapType.IndividualCubemaps && i == 0)
            {
                AddLine(envMapTransform.Translation, bounds.Center, new(0.0f, 1.0f, 0.0f, 1.0f));
                i++;
                continue;
            }

            var fractionToTen = Math.Min((float)i / 10, 1.0f);
            var color = new Color32(1.0f, fractionToTen, fractionToTen, 1.0f);
            AddLine(envMapTransform.Translation, bounds.Center, color);
            i++;
        }
    }

    private static int ClosestVertexInView(Camera camera, ReadOnlySpan<Vector3> vertices)
    {
        var minDistance = float.MaxValue;
        var closestIndex = -1;

        for (var i = 0; i < vertices.Length; i++)
        {
            if (camera.ViewFrustum.Intersects(vertices[i]))
            {
                var distance = Vector3.DistanceSquared(vertices[i], camera.Location);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestIndex = i;
                }
            }
        }

        return closestIndex;
    }

    // Solid where seen, and faint over the scene
    private void AddLine(Vector3 start, Vector3 end, Color32 color)
        => vertices.AddLine(start, end, color with { A = HiddenLineAlpha }, HelperPasses.Both);

    private void AddBox(Camera camera, TextRenderer textRenderer, in Matrix4x4 transform, in AABB box, Color32 color, bool showSize = false)
    {
        ReadOnlySpan<Vector3> c =
        [
            Vector3.Transform(new Vector3(box.Min.X, box.Min.Y, box.Min.Z), transform),
            Vector3.Transform(new Vector3(box.Max.X, box.Min.Y, box.Min.Z), transform),
            Vector3.Transform(new Vector3(box.Max.X, box.Max.Y, box.Min.Z), transform),
            Vector3.Transform(new Vector3(box.Min.X, box.Max.Y, box.Min.Z), transform),
            Vector3.Transform(new Vector3(box.Min.X, box.Min.Y, box.Max.Z), transform),
            Vector3.Transform(new Vector3(box.Max.X, box.Min.Y, box.Max.Z), transform),
            Vector3.Transform(new Vector3(box.Max.X, box.Max.Y, box.Max.Z), transform),
            Vector3.Transform(new Vector3(box.Min.X, box.Max.Y, box.Max.Z), transform),
        ];

        ReadOnlySpan<(int Start, int End)> Lines =
        [
            (0, 1), (1, 2), (2, 3), (3, 0), // Bottom face
            (4, 5), (5, 6), (6, 7), (7, 4), // Top face
            (0, 4), (1, 5), (2, 6), (3, 7), // Vertical edges
        ];

        var closestIndex = showSize ? ClosestVertexInView(camera, c) : -1;

        for (var i = 0; i < Lines.Length; i++)
        {
            var line = Lines[i];

            if (closestIndex == line.Start || closestIndex == line.End)
            {
                var axis = i >= 8 ? 2 : i % 2;

                var axisColor = axis switch
                {
                    0 => new Color32(1.0f, 0.2f, 0.2f, 1),
                    1 => new Color32(0.2f, 0.8f, 0.2f, 1),
                    2 => new Color32(0.2f, 0.2f, 1.0f, 1),
                    _ => color,
                };

                var (v0, v1) = (c[line.Start], c[line.End]);
                var length = Vector3.Distance(v0, v1);

                textRenderer.AddTextBillboard(Vector3.Lerp(v0, v1, 0.5f), new TextRenderer.TextRenderRequest
                {
                    Scale = 13f,
                    Color = axisColor,
                    Text = length.ToString("0.##", CultureInfo.InvariantCulture),
                    CenterVertical = true,
                    CenterHorizontal = true,
                }, camera);

                AddLine(c[line.Start], c[line.End], axisColor);
                continue;
            }

            AddLine(c[line.Start], c[line.End], color);
        }
    }
}
