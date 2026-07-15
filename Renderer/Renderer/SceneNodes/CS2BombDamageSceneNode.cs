using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes.GenericData.CS2;

namespace ValveResourceFormat.Renderer.SceneNodes;

/// <summary>
/// Scene node visualizing CS2 baked bomb damage data.
/// </summary>
public class CS2BombDamageSceneNode : SceneNode
{
    private const float HalfQuadSize = 12.0f;

    private static readonly Vector3[] VertexOffsets =
    [
        new(-HalfQuadSize, -HalfQuadSize, 0.0f),
        new(HalfQuadSize, -HalfQuadSize, 0.0f),
        new(HalfQuadSize, HalfQuadSize, 0.0f),
        new(-HalfQuadSize, HalfQuadSize, 0.0f),
    ];

    private readonly Shader shader;
    private readonly RenderTexture renderTexture;
    private readonly int vaoHandle;
    private readonly int vboHandle;
    private readonly int iboHandle;

    private const int VertexPositionOffset = 0;
    private const int VertexUVOffset = 12;
    private const int VertexColorOffset = 20;
    private const int VertexPhaseOffset = 24;
    private const int VertexSize = 28;

    private int indicesCount;

    private Vector3 boundsMin;
    private Vector3 boundsMax;

    [StructLayout(LayoutKind.Explicit, Size = VertexSize)]
    private struct VertexFormat
    {
        [FieldOffset(VertexPositionOffset)]
        public Vector3 Position;
        [FieldOffset(VertexUVOffset)]
        public Vector2 UVs;
        [FieldOffset(VertexColorOffset)]
        public Color32 Color;
        [FieldOffset(VertexPhaseOffset)]
        public float Phase;
    }

    /// <summary>
    /// Initializes a baked bomb damage visualization scene node for a specific bombsite.
    /// </summary>
    /// <param name="scene">The scene this node belongs to.</param>
    /// <param name="bombDamageData">Baked bomb damage data.</param>
    /// <param name="bombsiteIndex">Index of the bombsite. Index 0 is not guaranteed to be bombsite A.</param>
    public CS2BombDamageSceneNode(Scene scene, BombDamage bombDamageData, int bombsiteIndex) : base(scene)
    {
        shader = Scene.RendererContext.ShaderLoader.LoadShader("vrf.cs2_baked_bomb_damage");

        GL.CreateVertexArrays(1, out vaoHandle);

        var buffers = new int[2];
        GL.CreateBuffers(2, buffers);
        iboHandle = buffers[0];
        vboHandle = buffers[1];

        LayerName = $"Bombsite {bombsiteIndex} baked damage data";

        InitializeVAO(shader.Program);
        Initialize(bombDamageData, bombsiteIndex);
        renderTexture = InitializeTexture();
    }

    private RenderTexture InitializeTexture()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Renderer.Resources.arrow.vtex_c");
        using var resource = new Resource()
        {
            FileName = "arrow.vtex_c"
        };

        Debug.Assert(stream != null);
        resource.Read(stream);

        var renderTexture = Scene.RendererContext.MaterialLoader.LoadTexture(resource, isViewerRequest: true);
        renderTexture.SetWrapMode(TextureWrapMode.ClampToEdge);
        return renderTexture;
    }

    private void InitializeVAO(int shaderProgram)
    {
        var positionAttributeLocation = GL.GetAttribLocation(shaderProgram, "aVertexPosition");
        var colorAttributeLocation = GL.GetAttribLocation(shaderProgram, "aVertexColor");
        var uvAttributeLocation = GL.GetAttribLocation(shaderProgram, "aTexCoords");
        var phaseAttributeLocation = GL.GetAttribLocation(shaderProgram, "aPhase");

        if (positionAttributeLocation >= 0)
        {
            GL.EnableVertexArrayAttrib(vaoHandle, positionAttributeLocation);
            GL.VertexArrayAttribFormat(vaoHandle, positionAttributeLocation, 3, VertexAttribType.Float, false, VertexPositionOffset);
            GL.VertexArrayAttribBinding(vaoHandle, positionAttributeLocation, 0);
        }
        if (uvAttributeLocation >= 0)
        {
            GL.EnableVertexArrayAttrib(vaoHandle, uvAttributeLocation);
            GL.VertexArrayAttribFormat(vaoHandle, uvAttributeLocation, 2, VertexAttribType.Float, false, VertexUVOffset);
            GL.VertexArrayAttribBinding(vaoHandle, uvAttributeLocation, 0);
        }
        if (colorAttributeLocation >= 0)
        {
            GL.EnableVertexArrayAttrib(vaoHandle, colorAttributeLocation);
            GL.VertexArrayAttribFormat(vaoHandle, colorAttributeLocation, 4, VertexAttribType.UnsignedByte, true, VertexColorOffset);
            GL.VertexArrayAttribBinding(vaoHandle, colorAttributeLocation, 0);
        }
        if (phaseAttributeLocation >= 0)
        {
            GL.EnableVertexArrayAttrib(vaoHandle, phaseAttributeLocation);
            GL.VertexArrayAttribFormat(vaoHandle, phaseAttributeLocation, 1, VertexAttribType.Float, false, VertexPhaseOffset);
            GL.VertexArrayAttribBinding(vaoHandle, phaseAttributeLocation, 0);
        }
    }

    private void Initialize(BombDamage bombDamageData, int bombsiteIndex)
    {
        var vertices = new List<VertexFormat>(bombDamageData.Positions.Length * 4);
        var indices = new List<int>(bombDamageData.Positions.Length * 6);

        for (var i = 0; i < bombDamageData.Positions.Length; i++)
        {
            if (vertices.Count == 0)
            {
                boundsMin = boundsMax = bombDamageData.Positions[i];
            }
            AddFace(vertices, indices, bombDamageData, i, bombsiteIndex);
        }

        indicesCount = indices.Count;
        BoundingBox = new AABB(boundsMin, boundsMax);

        GL.VertexArrayVertexBuffer(vaoHandle, 0, vboHandle, 0, VertexSize);
        GL.NamedBufferData(vboHandle, vertices.Count * VertexSize, ListAccessors<VertexFormat>.GetBackingArray(vertices), BufferUsageHint.StaticDraw);

        GL.VertexArrayElementBuffer(vaoHandle, iboHandle);
        GL.NamedBufferData(iboHandle, indices.Count * sizeof(int), ListAccessors<int>.GetBackingArray(indices), BufferUsageHint.StaticDraw);

#if DEBUG
        var vaoLabel = nameof(CS2BombDamageSceneNode);
        GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vaoHandle, vaoLabel.Length, vaoLabel);
        GL.ObjectLabel(ObjectLabelIdentifier.Buffer, vboHandle, vaoLabel.Length, vaoLabel);
        GL.ObjectLabel(ObjectLabelIdentifier.Buffer, iboHandle, vaoLabel.Length, vaoLabel);
#endif
    }

    private static void AddFaceIndices(List<int> indices, int baseFaceIndex)
    {
        indices.Add(baseFaceIndex + 0);
        indices.Add(baseFaceIndex + 1);
        indices.Add(baseFaceIndex + 2);
        indices.Add(baseFaceIndex + 0);
        indices.Add(baseFaceIndex + 2);
        indices.Add(baseFaceIndex + 3);
    }

    private void AddFace(List<VertexFormat> vertices, List<int> indices, BombDamage bombDamageData, int bombPositionIndex, int bombsiteIndex)
    {
        var basePosition = bombDamageData.Positions[bombPositionIndex];
        var damage = bombDamageData.GetBombsiteDamageValue(bombPositionIndex, bombsiteIndex);

        var color = GetFaceColor(bombDamageData.Bombsites[bombsiteIndex], damage, basePosition);

        AddFaceIndices(indices, vertices.Count);

        var rotation = damage.Rotation;

        Span<Vector3> faceVertices = stackalloc Vector3[4];
        VertexOffsets.AsSpan().CopyTo(faceVertices);
        for (var i = 0; i < faceVertices.Length; i++)
        {
            faceVertices[i] = Vector3.Transform(faceVertices[i], rotation);
        }

        AddVertex(vertices, basePosition + faceVertices[0], Vector2.Zero, color, damage.Phase);
        AddVertex(vertices, basePosition + faceVertices[1], Vector2.UnitX, color, damage.Phase);
        AddVertex(vertices, basePosition + faceVertices[2], Vector2.One, color, damage.Phase);
        AddVertex(vertices, basePosition + faceVertices[3], Vector2.UnitY, color, damage.Phase);
    }

    private void AddVertex(List<VertexFormat> vertices, Vector3 position, Vector2 uvs, Color32 color, float phase)
    {
        boundsMax = Vector3.Max(boundsMax, position);
        boundsMin = Vector3.Min(boundsMin, position);
        vertices.Add(new VertexFormat
        {
            Position = position,
            UVs = uvs,
            Color = color,
            Phase = phase,
        });
    }

    // White inside the bombsite, otherwise a gradient from green (lethal, 100+ damage) through yellow to red (no damage)
    private static Color32 GetFaceColor(in BombDamageBombsite bombsite, in BombDamageDamageValue damage, Vector3 position)
    {
        if (position == Vector3.Clamp(position, bombsite.BoundsMin, bombsite.BoundsMax))
        {
            return Color32.White;
        }

        var t = Math.Clamp(BombDamage.CalculateDamage(bombsite, damage) / 100f, 0f, 1f);
        var r = Math.Min(1f, 2f * (1f - t));
        var g = Math.Min(1f, 2f * t);
        return new Color32(r, g, 0f, 1f);
    }

    /// <inheritdoc/>
    public override void Render(Scene.RenderContext context)
    {
        if (context.RenderPass != RenderPass.Translucent)
        {
            return;
        }

        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);

        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        GL.BindVertexArray(vaoHandle);
        var renderShader = context.ReplacementShader ?? shader;
        renderShader.Use();
        renderShader.SetUniform3x4("transform", Matrix4x4.Identity);
        renderShader.SetTexture(0, 0, renderTexture);

        GL.DrawElementsInstancedBaseInstance(PrimitiveType.Triangles, indicesCount, DrawElementsType.UnsignedInt, 0, 1, Id);

        GL.UseProgram(0);
        GL.BindVertexArray(0);

        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);
    }

    /// <summary>
    /// Adds visualization scene nodes of a <see cref="BombDamage"/> to a <see cref="Scene"/>. Each bombsite gets its own <see cref="CS2BombDamageSceneNode"/>.
    /// </summary>
    public static void AddBakedBombDamageToScene(BombDamage? bombDamageData, Scene scene)
    {
        if (bombDamageData == null || scene == null)
        {
            return;
        }

        for (var i = 0; i < bombDamageData.Bombsites.Length; i++)
        {
            var sceneNode = new CS2BombDamageSceneNode(scene, bombDamageData, i);
            scene.Add(sceneNode, false);

            var bombsite = bombDamageData.Bombsites[i];
            AddBoundsLines(scene, new AABB(bombsite.BoundsMin, bombsite.BoundsMax), Color32.Red, sceneNode.LayerName);
        }
    }

    /// <summary>
    /// Adds the 12 wireframe edges of an AABB to the scene as <see cref="LineSceneNode"/> segments.
    /// </summary>
    private static void AddBoundsLines(Scene scene, in AABB box, Color32 color, string? layerName)
    {
        var min = box.Min;
        var max = box.Max;

        Span<Vector3> corners =
        [
            new(min.X, min.Y, min.Z),
            new(max.X, min.Y, min.Z),
            new(max.X, max.Y, min.Z),
            new(min.X, max.Y, min.Z),
            new(min.X, min.Y, max.Z),
            new(max.X, min.Y, max.Z),
            new(max.X, max.Y, max.Z),
            new(min.X, max.Y, max.Z),
        ];

        ReadOnlySpan<int> edges =
        [
            0, 1, 1, 2, 2, 3, 3, 0, // bottom face
            4, 5, 5, 6, 6, 7, 7, 4, // top face
            0, 4, 1, 5, 2, 6, 3, 7, // vertical edges
        ];

        for (var i = 0; i < edges.Length; i += 2)
        {
            var line = new LineSceneNode(scene, corners[edges[i]], corners[edges[i + 1]], color, color)
            {
                LayerName = layerName,
            };
            scene.Add(line, false);
        }
    }

    /// <inheritdoc/>
    public override void Delete()
    {
        base.Delete();
        GL.DeleteVertexArray(vaoHandle);

        GL.DeleteBuffers(2, [iboHandle, vboHandle]);
    }
}
