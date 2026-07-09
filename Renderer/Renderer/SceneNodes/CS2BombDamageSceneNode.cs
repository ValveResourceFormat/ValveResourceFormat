using System.Buffers;
using System.Drawing;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.GameSpecific.CS2.BombDamageData;

namespace ValveResourceFormat.Renderer.SceneNodes;

public class CS2BombDamageSceneNode : SceneNode
{
    private const float QuadSize = 8.0f;

    /// <summary>Gets the shader used to render this shape.</summary>
    protected Shader shader { get; init; }
    private readonly int vaoHandle;
    private readonly int vboHandle;
    private readonly int iboHandle;

    private const int VertexPositionOffset = 0;
    private const int VertexUVOffset = 12;
    private const int VertexColorOffset = 20;
    private const int VertexSize = 24;

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
        public int Color;
    }

    public CS2BombDamageSceneNode(Scene scene, BombDamageData bombDamageData, int bombsiteIndex) : base(scene)
    {
        shader = Scene.RendererContext.ShaderLoader.LoadShader("vrf.cs2_baked_bomb_damage");

        GL.CreateVertexArrays(1, out vaoHandle);

        var buffers = new int[2];
        GL.CreateBuffers(buffers.Length, buffers);
        iboHandle = buffers[0];
        vboHandle = buffers[1];

        LayerName = $"Bombsite {(char)('A' + bombsiteIndex)} baked damage data";

        InitializeVAO(shader.Program);
        Initialize(bombDamageData, bombsiteIndex);
    }

    private void InitializeVAO(int shaderProgram)
    {
        var positionAttributeLocation = GL.GetAttribLocation(shaderProgram, "aVertexPosition");
        var colorAttributeLocation = GL.GetAttribLocation(shaderProgram, "aVertexColor");
        var uvAttributeLocation = GL.GetAttribLocation(shaderProgram, "aTexCoords");

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
                boundsMin = boundsMax = bombDamageData.Positions[i].Position;
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
        var basePosition = bombDamageData.Positions[bombPositionIndex].Position;
        var damage = bombDamageData.GetBombsiteDamageValue(bombPositionIndex, bombsiteIndex);
        var color = Color.FromArgb(damage.DistanceUnk1, damage.DistanceUnk2, damage.Yaw, damage.Pitch).ToArgb();

        indices[indicesCount++] = verticesCount + 0;
        indices[indicesCount++] = verticesCount + 1;
        indices[indicesCount++] = verticesCount + 2;
        indices[indicesCount++] = verticesCount + 0;
        indices[indicesCount++] = verticesCount + 2;
        indices[indicesCount++] = verticesCount + 3;

        AddVertex(vertices, ref verticesCount, indices, ref indicesCount, basePosition + Vector3.Zero * QuadSize, Vector2.Zero, color);
        AddVertex(vertices, ref verticesCount, indices, ref indicesCount, basePosition + Vector3.UnitX * QuadSize, Vector2.UnitX, color);
        AddVertex(vertices, ref verticesCount, indices, ref indicesCount, basePosition + (Vector3.UnitX + Vector3.UnitY) * QuadSize, Vector2.One, color);
        AddVertex(vertices, ref verticesCount, indices, ref indicesCount, basePosition + Vector3.UnitY * QuadSize, Vector2.UnitY, color);
    }

    private void AddVertex(VertexFormat[] vertices, ref int verticesCount, int[] indices, ref int indicesCount, Vector3 position, Vector2 uvs, int color)
    {
        boundsMax = Vector3.Max(boundsMax, position);
        boundsMin = Vector3.Min(boundsMin, position);
        vertices[verticesCount].Position = position;
        vertices[verticesCount].UVs = uvs;
        vertices[verticesCount].Color = color;
        verticesCount++;
    }

    private static int RGBAColor(byte r, byte g, byte b, byte a)
    {
        return r << 24 | g << 16 | b << 8 | a;
    }

    public override void Render(Scene.RenderContext context)
    {
        GL.BindVertexArray(vaoHandle);

        var renderShader = context.ReplacementShader ?? shader;
        renderShader.Use();
        renderShader.SetUniform3x4("transform", Matrix4x4.Identity);

        GL.DrawElementsInstancedBaseInstance(PrimitiveType.Triangles, indicesCount, DrawElementsType.UnsignedInt, 0, 1, Id);

        GL.UseProgram(0);
        GL.BindVertexArray(0);
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
