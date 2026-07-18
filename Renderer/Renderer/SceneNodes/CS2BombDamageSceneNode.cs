using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.GenericData.CS2;
using ValveResourceFormat.Serialization.KeyValues;

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

    private readonly RenderMaterial material;
    private readonly string meshName;
    private int vaoHandle;

    private const int VertexPositionOffset = 0;
    private const int VertexUVOffset = 12;
    private const int VertexColorOffset = 20;
    private const int VertexPhaseOffset = 24;
    private const int VertexSize = 28;

    private static readonly VBIB.RenderInputLayoutField[] InputLayout =
    [
        new() { SemanticName = "POSITION", Format = DXGI_FORMAT.R32G32B32_FLOAT, Offset = VertexPositionOffset },
        new() { SemanticName = "TEXCOORD", Format = DXGI_FORMAT.R32G32_FLOAT, Offset = VertexUVOffset },
        new() { SemanticName = "COLOR", Format = DXGI_FORMAT.R8G8B8A8_UNORM, Offset = VertexColorOffset },
        new() { SemanticName = "PHASE", Format = DXGI_FORMAT.R32_FLOAT, Offset = VertexPhaseOffset },
    ];

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
    /// <param name="bombsiteLabel">Display label for the bombsite ("A", "B", or "?" when unresolved).</param>
    public CS2BombDamageSceneNode(Scene scene, BombDamage bombDamageData, int bombsiteIndex, RenderTexture renderTexture, string bombsiteLabel) : base(scene)
    {
        var shader = Scene.RendererContext.ShaderLoader.LoadShader("vrf.cs2_baked_bomb_damage");
        meshName = $"{bombDamageData.Resource.FileName}:site{bombsiteIndex}";

        material = new RenderMaterial(shader);
        material.Material.IntParams["F_TRANSLUCENT"] = 1;
        material.Material.IntParams["F_RENDER_BACKFACES"] = 1;
        material.Material.IntParams["F_DISABLE_Z_BUFFERING"] = 1;
        material.LoadRenderState();
        material.Textures["g_tColor"] = renderTexture;

        LayerName = $"Bombsite {bombsiteLabel} blast flow map";

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

        var renderTexture = scene.RendererContext.MaterialLoader.LoadTexture(resource, srgbRead: true, isViewerRequest: true);
        renderTexture.SetWrapMode(TextureWrapMode.ClampToEdge);
        return renderTexture;
    }

    private void Initialize(BombDamage bombDamageData, int bombsiteIndex)
    {
        var positions = bombDamageData.Positions;
        var bombsite = bombDamageData.Bombsites[bombsiteIndex];
        var bombsiteBounds = new AABB(bombsite.BoundsMin, bombsite.BoundsMax);
        var damageValuesOffset = positions.Length * bombsiteIndex;

        var vertexCount = positions.Length * 4;
        var indexCount = positions.Length * 6;

        var vertexData = new byte[vertexCount * VertexSize];
        var indexData = new byte[indexCount * sizeof(int)];
        var vertices = MemoryMarshal.Cast<byte, VertexFormat>(vertexData.AsSpan());
        var indices = MemoryMarshal.Cast<byte, int>(indexData.AsSpan());

        if (positions.Length > 0)
        {
            boundsMin = boundsMax = positions[0];
        }

        for (var i = 0; i < positions.Length; i++)
        {
            AddFace(vertices, indices, i, positions[i], bombDamageData.DamageValues[damageValuesOffset + i], bombsite, bombsiteBounds);
        }

        indicesCount = indexCount;
        BoundingBox = new AABB(boundsMin, boundsMax);

        var vbib = new VBIB { Resource = null! };
        vbib.VertexBuffers.Add(new VBIB.OnDiskBufferData
        {
            ElementCount = (uint)vertexCount,
            ElementSizeInBytes = VertexSize,
            InputLayoutFields = InputLayout,
            Data = vertexData,
        });
        vbib.IndexBuffers.Add(new VBIB.OnDiskBufferData
        {
            ElementCount = (uint)indexCount,
            ElementSizeInBytes = sizeof(int),
            InputLayoutFields = [],
            Data = indexData,
        });

        var meshBufferCache = Scene.RendererContext.MeshBufferCache;
        var gpuBuffers = meshBufferCache.CreateVertexIndexBuffers(meshName, vbib);

        // why do we have to create this
        VertexDrawBuffer[] vertexDrawBuffers =
        [
            new VertexDrawBuffer
            {
                Handle = gpuBuffers.VertexBuffers[0],
                ElementSizeInBytes = VertexSize,
                InputLayoutFields = InputLayout,
            },
        ];

        vaoHandle = meshBufferCache.GetVertexArrayObject(meshName, vertexDrawBuffers, material, gpuBuffers.IndexBuffers[0]);
    }

    private void AddFace(Span<VertexFormat> vertices, Span<int> indices, int positionIndex, Vector3 basePosition, in BombDamageDamageValue damage, in BombDamageBombsite bombsite, in AABB bombsiteBounds)
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

        var renderShader = context.ReplacementShader ?? material.Shader;
        renderShader.Use();
        GL.BindVertexArray(vaoHandle);
        material.Render(renderShader);
        renderShader.SetUniform3x4("transform", Matrix4x4.Identity);

        GL.DrawElementsInstancedBaseInstance(PrimitiveType.Triangles, indicesCount, DrawElementsType.UnsignedInt, 0, 1, Id);

        material.PostRender();
        GL.BindVertexArray(0);
        GL.UseProgram(0);
    }

    private static string GetBombsiteDesignation(SceneNode bombTarget) => bombTarget.EntityData?.GetStringProperty("bomb_site_designation") switch
    {
        "0" => "A",
        "1" => "B",
        _ => "?",
    };

    /// <summary>
    /// Adds visualization scene nodes of a <see cref="BombDamage"/> to a <see cref="Scene"/>. Each bombsite gets its own <see cref="CS2BombDamageSceneNode"/>.
    /// The baked data carries no site letters; at detonation the first site whose bounds, expanded by 32 units,
    /// contain the bomb position is used, so each site's plant trigger volume must intersect its bounds. Sites are
    /// labeled from the <c>func_bomb_target</c> volumes intersecting their bounds, "?" unless exactly one designation matches.
    /// </summary>
    public static void AddBakedBombDamageToScene(BombDamage? bombDamageData, Scene scene)
    {
        if (bombDamageData == null || scene == null)
        {
            return;
        }

        var bombTargets = scene.AllNodes
            .Where(static n => n.EntityData?.GetStringProperty("classname") == "func_bomb_target")
            .ToList();

        var arrowTexture = LoadArrowTexture(scene);

        for (var i = 0; i < bombDamageData.Bombsites.Length; i++)
        {
            var bombsite = bombDamageData.Bombsites[i];
            var siteBounds = new AABB(bombsite.BoundsMin - new Vector3(32f), bombsite.BoundsMax + new Vector3(32f));
            var designations = bombTargets
                .Where(n => n.BoundingBox.Intersects(siteBounds))
                .Select(GetBombsiteDesignation)
                .Distinct()
                .ToList();
            var label = designations.Count == 1 ? designations[0] : "?";

            var sceneNode = new CS2BombDamageSceneNode(scene, bombDamageData, i, arrowTexture, label);
            scene.Add(sceneNode, false);
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
        Scene.RendererContext.MeshBufferCache.DeleteVertexIndexBuffers(meshName);
    }
}
