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

    private static readonly Vector2[] VertexUVs =
    [
        Vector2.Zero,
        Vector2.UnitX,
        Vector2.One,
        Vector2.UnitY,
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
    /// <param name="renderTexture">Texture drawn on each damage value quad.</param>
    public CS2BombDamageSceneNode(Scene scene, BombDamage bombDamageData, int bombsiteIndex, RenderTexture renderTexture) : base(scene)
    {
        shader = Scene.RendererContext.ShaderLoader.LoadShader("vrf.cs2_baked_bomb_damage");
        this.renderTexture = renderTexture;

        GL.CreateVertexArrays(1, out vaoHandle);

        var buffers = new int[2];
        GL.CreateBuffers(2, buffers);
        iboHandle = buffers[0];
        vboHandle = buffers[1];

        LayerName = $"Bombsite {bombsiteIndex} baked damage data";

        InitializeVAO(shader.Program);
        Initialize(bombDamageData, bombsiteIndex);
    }

    private static RenderTexture LoadArrowTexture(Scene scene)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Renderer.Resources.arrow.vtex_c");
        using var resource = new Resource()
        {
            FileName = "arrow.vtex_c"
        };

        Debug.Assert(stream != null);
        resource.Read(stream);

        var renderTexture = scene.RendererContext.MaterialLoader.LoadTexture(resource, isViewerRequest: true);
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
        var positions = bombDamageData.Positions;
        var bombsite = bombDamageData.Bombsites[bombsiteIndex];
        var bombsiteBounds = new AABB(bombsite.BoundsMin, bombsite.BoundsMax);
        var damageValuesOffset = positions.Length * bombsiteIndex;

        var vertices = new VertexFormat[positions.Length * 4];
        var indices = new int[positions.Length * 6];

        if (positions.Length > 0)
        {
            boundsMin = boundsMax = positions[0];
        }

        for (var i = 0; i < positions.Length; i++)
        {
            AddFace(vertices, indices, i, positions[i], bombDamageData.DamageValues[damageValuesOffset + i], bombsite, bombsiteBounds);
        }

        indicesCount = indices.Length;
        BoundingBox = new AABB(boundsMin, boundsMax);

        GL.VertexArrayVertexBuffer(vaoHandle, 0, vboHandle, 0, VertexSize);
        GL.NamedBufferData(vboHandle, vertices.Length * VertexSize, vertices, BufferUsageHint.StaticDraw);

        GL.VertexArrayElementBuffer(vaoHandle, iboHandle);
        GL.NamedBufferData(iboHandle, indices.Length * sizeof(int), indices, BufferUsageHint.StaticDraw);

#if DEBUG
        var vaoLabel = nameof(CS2BombDamageSceneNode);
        GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vaoHandle, vaoLabel.Length, vaoLabel);
        GL.ObjectLabel(ObjectLabelIdentifier.Buffer, vboHandle, vaoLabel.Length, vaoLabel);
        GL.ObjectLabel(ObjectLabelIdentifier.Buffer, iboHandle, vaoLabel.Length, vaoLabel);
#endif
    }

    private void AddFace(VertexFormat[] vertices, int[] indices, int positionIndex, Vector3 basePosition, in BombDamageDamageValue damage, in BombDamageBombsite bombsite, in AABB bombsiteBounds)
    {
        var baseVertex = positionIndex * 4;
        var baseIndex = positionIndex * 6;

        indices[baseIndex + 0] = baseVertex + 0;
        indices[baseIndex + 1] = baseVertex + 1;
        indices[baseIndex + 2] = baseVertex + 2;
        indices[baseIndex + 3] = baseVertex + 0;
        indices[baseIndex + 4] = baseVertex + 2;
        indices[baseIndex + 5] = baseVertex + 3;

        var color = GetFaceColor(bombsite, bombsiteBounds, damage, basePosition);
        var rotation = damage.Rotation;

        for (var i = 0; i < 4; i++)
        {
            var position = basePosition + Vector3.Transform(VertexOffsets[i], rotation);
            boundsMax = Vector3.Max(boundsMax, position);
            boundsMin = Vector3.Min(boundsMin, position);

            vertices[baseVertex + i] = new VertexFormat
            {
                Position = position,
                UVs = VertexUVs[i],
                Color = color,
                Phase = damage.Phase,
            };
        }
    }

    // White inside the bombsite, otherwise a gradient from green (lethal, 100+ damage) through yellow to red (no damage)
    private static Color32 GetFaceColor(in BombDamageBombsite bombsite, in AABB bombsiteBounds, in BombDamageDamageValue damage, Vector3 position)
    {
        if (bombsiteBounds.Contains(position))
        {
            return Color32.White;
        }

        var t = MathUtils.Saturate(BombDamage.CalculateDamage(bombsite, damage) / 100f);
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

        var arrowTexture = LoadArrowTexture(scene);

        for (var i = 0; i < bombDamageData.Bombsites.Length; i++)
        {
            var sceneNode = new CS2BombDamageSceneNode(scene, bombDamageData, i, arrowTexture);
            scene.Add(sceneNode, false);

            var bombsite = bombDamageData.Bombsites[i];
            var boundsVertices = new List<SimpleVertex>(2 * 12);
            ShapeSceneNode.AddBox(boundsVertices, new AABB(bombsite.BoundsMin, bombsite.BoundsMax), Color32.Red);

            var boundsNode = new LineSceneNode(scene, [.. boundsVertices])
            {
                LayerName = sceneNode.LayerName,
            };
            scene.Add(boundsNode, false);
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
