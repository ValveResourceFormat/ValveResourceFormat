using System.Linq;
using SharpGLTF.Schema2;
using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace ValveResourceFormat.IO;

/// <summary>
/// Builds the glTF node hierarchy a skeleton becomes, and the point mesh importers recognise it by.
/// </summary>
public partial class GltfModelExporter
{
    private static (Node? skeletonNode, Node[]? joints) CreateGltfSkeleton(Scene scene, Skeleton skeleton, string modelName)
    {
        if (skeleton.Bones.Length == 0)
        {
            return (null, null);
        }

        var skeletonNode = scene.CreateNode(modelName);
        var boneNodes = new Dictionary<string, Node>();
        var joints = new Node[skeleton.Bones.Length];
        foreach (var root in skeleton.Roots)
        {
            CreateBonesRecursive(root, skeletonNode, ref joints, isRoot: true);
        }
        return (skeletonNode, joints);
    }

    private static void CreateBonesRecursive(Bone bone, Node parent, ref Node[] joints, bool isRoot)
    {
        var (translation, rotation) = BakeConversion(bone.Position, bone.Angle, isRoot);

        var node = parent.CreateNode(bone.Name);
        node.LocalTransform = node.LocalTransform.WithTranslation(translation).WithRotation(rotation);
        joints[bone.Index] = node;

        foreach (var child in bone.Children)
        {
            CreateBonesRecursive(child, node, ref joints, isRoot: false);
        }
    }

    private static void CreateSkeletonVisualizationMesh(ModelRoot exportedModel, Scene scene, Skeleton skeleton, Node[] joints, string skeletonName)
    {
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var boneIndicesShort = new List<ushort>();
        var boneWeights = new List<Vector4>();

        // Create a single vertex at each bone position
        foreach (var bone in skeleton.Bones)
        {
            var joint = joints[bone.Index];
            var worldMatrix = joint.WorldMatrix;
            var worldPosition = worldMatrix.Translation;

            positions.Add(worldPosition);
            normals.Add(Vector3.UnitY);
            boneIndicesShort.Add((ushort)bone.Index);
            boneIndicesShort.Add(0);
            boneIndicesShort.Add(0);
            boneIndicesShort.Add(0);
            boneWeights.Add(new Vector4(1.0f, 0, 0, 0));
        }

        var indices = Enumerable.Range(0, positions.Count).ToArray();

        var mesh = exportedModel.CreateMesh();
        var primitive = mesh.CreatePrimitive();

        var positionAccessor = CreateAccessor(exportedModel, [.. positions]);
        var normalAccessor = CreateAccessor(exportedModel, [.. normals]);
        var weightsAccessor = CreateUnorm8Accessor(exportedModel, [.. boneWeights]);
        var jointsAccessor = CreateJointsAccessor(exportedModel, [.. boneIndicesShort]);

        primitive.SetVertexAccessor("POSITION", positionAccessor);
        primitive.SetVertexAccessor("NORMAL", normalAccessor);
        primitive.SetVertexAccessor("JOINTS_0", jointsAccessor);
        primitive.SetVertexAccessor("WEIGHTS_0", weightsAccessor);

        SetIndices(primitive, PrimitiveType.POINTS, indices);

        // Reuse skeleton material if it already exists
        var material = exportedModel.LogicalMaterials.FirstOrDefault(m => m.Name == "skeleton_material");
        if (material == null)
        {
            material = exportedModel.CreateMaterial("skeleton_material");
            material.InitializePBRMetallicRoughness();
            SetMetallicRoughness(material, new Vector4(0.8f, 0.8f, 0.8f, 1.0f), metallicFactor: 0.0f);
            material.Alpha = AlphaMode.OPAQUE;
        }

        primitive.Material = material;

        var skin = exportedModel.CreateSkin();
        skin.BindJoints(Matrix4x4.Identity, joints);

        var meshNode = scene.CreateNode($"{skeletonName}.empty_mesh_reference");
        meshNode.Mesh = mesh;
        meshNode.Skin = skin;
    }
}
