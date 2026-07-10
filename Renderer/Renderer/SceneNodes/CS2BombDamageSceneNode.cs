using System.Buffers;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.GameSpecific.CS2.BombDamageData;

namespace ValveResourceFormat.Renderer.SceneNodes;

public class CS2BombDamageSceneNode : SceneNode
{
    private RenderTexture renderTexture;

    private const float QuadSize = 12.0f;
    private const float HalfQuadSize = QuadSize / 2.0f;

    private static Vector3[] VertexOffsets =
    {
        new Vector3(-HalfQuadSize, -HalfQuadSize, 0.0f),
        new Vector3(HalfQuadSize, -HalfQuadSize, 0.0f),
        new Vector3(HalfQuadSize, HalfQuadSize, 0.0f),
        new Vector3(-HalfQuadSize, HalfQuadSize, 0.0f),
    };

    /// <summary>Gets the shader used to render this shape.</summary>
    protected Shader shader { get; init; }
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

    private DateTime creationTime;

    [StructLayout(LayoutKind.Explicit, Size = VertexSize)]
    private struct VertexFormat
    {
        [FieldOffset(VertexPositionOffset)]
        public Vector3 Position;
        [FieldOffset(VertexUVOffset)]
        public Vector2 UVs;
        [FieldOffset(VertexColorOffset)]
        public int Color;
        [FieldOffset(VertexPhaseOffset)]
        public float Phase;
    }

    public CS2BombDamageSceneNode(Scene scene, BombDamageData bombDamageData, int bombsiteIndex) : base(scene)
    {
        creationTime = DateTime.Now;

        shader = Scene.RendererContext.ShaderLoader.LoadShader("vrf.cs2_baked_bomb_damage");

        GL.CreateVertexArrays(1, out vaoHandle);

        var buffers = new int[2];
        GL.CreateBuffers(buffers.Length, buffers);
        iboHandle = buffers[0];
        vboHandle = buffers[1];

        LayerName = $"Bombsite {(char)('A' + bombsiteIndex)} baked damage data";

        InitializeVAO(shader.Program);
        Initialize(bombDamageData, bombsiteIndex);
        InitializeTexture();
    }

    private void InitializeTexture()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Renderer.Resources.arrow.vtex_c");
        using var resource = new Resource()
        {
            FileName = "arrow.vtex_c"
        };

        Debug.Assert(stream != null);
        resource.Read(stream);

        renderTexture = Scene.RendererContext.MaterialLoader.LoadTexture(resource, isViewerRequest: true);
        renderTexture.SetWrapMode(TextureWrapMode.Clamp);
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

    private void Initialize(BombDamageData bombDamageData, int bombsiteIndex)
    {
        var vertices = ArrayPool<VertexFormat>.Shared.Rent(bombDamageData.Positions.Length * 4);
        var indices = ArrayPool<int>.Shared.Rent(bombDamageData.Positions.Length * 6);

        var verticesCount = 0;
        for (var i = 0; i < bombDamageData.Positions.Length; i++)
        {
            if (verticesCount == 0)
            {
                boundsMin = boundsMax = bombDamageData.Positions[i];
            }
            AddFace(vertices, ref verticesCount, indices, ref indicesCount, bombDamageData, i, bombsiteIndex);
        }

        BoundingBox = new AABB(boundsMin, boundsMax);

        GL.VertexArrayVertexBuffer(vaoHandle, 0, vboHandle, 0, VertexSize);
        GL.NamedBufferData(vboHandle, verticesCount * VertexSize, vertices, BufferUsageHint.StaticDraw);

        GL.VertexArrayElementBuffer(vaoHandle, iboHandle);
        GL.NamedBufferData(iboHandle, indicesCount * sizeof(int), indices, BufferUsageHint.StaticDraw);

#if DEBUG
        var vaoLabel = nameof(CS2BombDamageSceneNode);
        GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vaoHandle, vaoLabel.Length, vaoLabel);
        GL.ObjectLabel(ObjectLabelIdentifier.Buffer, vboHandle, vaoLabel.Length, vaoLabel);
        GL.ObjectLabel(ObjectLabelIdentifier.Buffer, iboHandle, vaoLabel.Length, vaoLabel);
#endif
    }

    private void AddFace(VertexFormat[] vertices, ref int verticesCount, int[] indices, ref int indicesCount, BombDamageData bombDamageData, int bombPositionIndex, int bombsiteIndex)
    {
        var basePosition = bombDamageData.Positions[bombPositionIndex];
        var damage = bombDamageData.GetBombsiteDamageValue(bombPositionIndex, bombsiteIndex);

        var phase = damage.DistanceUnk1 / 255.0f + damage.DistanceUnk2;

        var color = GetFaceColor(damage);

        indices[indicesCount++] = verticesCount + 0;
        indices[indicesCount++] = verticesCount + 1;
        indices[indicesCount++] = verticesCount + 2;
        indices[indicesCount++] = verticesCount + 0;
        indices[indicesCount++] = verticesCount + 2;
        indices[indicesCount++] = verticesCount + 3;

        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, float.DegreesToRadians(damage.Yaw)) * Quaternion.CreateFromAxisAngle(Vector3.UnitY, float.DegreesToRadians(damage.Pitch));
        Span<Vector3> faceVertices = stackalloc Vector3[4];
        VertexOffsets.AsSpan().CopyTo(faceVertices);
        RotateVectors(faceVertices, rotation);

        AddVertex(vertices, ref verticesCount, indices, ref indicesCount, basePosition + faceVertices[0], Vector2.Zero, color, phase);
        AddVertex(vertices, ref verticesCount, indices, ref indicesCount, basePosition + faceVertices[1], Vector2.UnitX, color, phase);
        AddVertex(vertices, ref verticesCount, indices, ref indicesCount, basePosition + faceVertices[2], Vector2.One, color, phase);
        AddVertex(vertices, ref verticesCount, indices, ref indicesCount, basePosition + faceVertices[3], Vector2.UnitY, color, phase);
    }

    private static void RotateVectors(Span<Vector3> vectors, Quaternion quaternion)
    {
        for (var i = 0; i < vectors.Length; i++)
        {
            vectors[i] = Vector3.Transform(vectors[i], quaternion);
        }
    }

    private void AddVertex(VertexFormat[] vertices, ref int verticesCount, int[] indices, ref int indicesCount, Vector3 position, Vector2 uvs, int color, float phase)
    {
        boundsMax = Vector3.Max(boundsMax, position);
        boundsMin = Vector3.Min(boundsMin, position);
        vertices[verticesCount].Position = position;
        vertices[verticesCount].UVs = uvs;
        vertices[verticesCount].Color = color;
        vertices[verticesCount].Phase = phase;
        verticesCount++;
    }

    private static int GetFaceColor(BombDamageDataDamageValue damage)
    {
        if (damage.DistanceUnk1 + damage.DistanceUnk2 == 0)
        {
            return RGBAColor(255, 255, 255, 255);
        }
        else
        {
            return RGBAColor(255, 255, 0, 255);
        }
    }

    private static int RGBAColor(byte r, byte g, byte b, byte a)
    {
        return a << 24 | b << 16 | g << 8 | r << 0;
    }

    public override void Render(Scene.RenderContext context)
    {
        GL.Disable(EnableCap.DepthTest);

        GL.BindVertexArray(vaoHandle);
        GL.Enable(EnableCap.Blend);
        GL.Disable(EnableCap.CullFace);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        var renderShader = context.ReplacementShader ?? shader;
        renderShader.Use();
        var time = (float)(DateTime.Now - creationTime).TotalSeconds;
        renderShader.SetUniform1("time", time);
        renderShader.SetUniform3x4("transform", Matrix4x4.Identity);
        renderShader.SetTexture(0, 0, renderTexture);

        GL.DrawElementsInstancedBaseInstance(PrimitiveType.Triangles, indicesCount, DrawElementsType.UnsignedInt, 0, 1, Id);

        GL.Enable(EnableCap.CullFace);
        GL.UseProgram(0);
        GL.BindVertexArray(0);
        GL.Enable(EnableCap.DepthTest);
    }

    /// <summary>
    /// Adds visualization scene nodes of a <see cref="BombDamageData"/> to a <see cref="Scene"/>. Each bombsite gets its own <see cref="CS2BombDamageSceneNode"/>.
    /// </summary>
    public static void AddBakedBombDamageToScene(BombDamageData? bombDamageData, Scene scene)
    {
        if (bombDamageData == null || scene == null)
        {
            return;
        }

        for (var i = 0; i < bombDamageData.Bombsites.Length; i++)
        {
            var sceneNode = new CS2BombDamageSceneNode(scene, bombDamageData, i);
            scene.Add(sceneNode, false);
        }
    }
}
