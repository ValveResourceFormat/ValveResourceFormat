using SharpGLTF.Schema2;
using ValveResourceFormat.NavMesh;

namespace ValveResourceFormat.IO;

/// <summary>
/// Navigation mesh export functionality for the GLTF model exporter.
/// Converts navigation mesh areas and ladders into GLTF geometry.
/// </summary>
public partial class GltfModelExporter
{
    private ModelRoot BuildNavMeshModel(string resourceName, NavMeshFile navMesh)
    {
        var exportedModel = CreateModelRoot(resourceName, out var scene);
        LoadNavMesh(exportedModel, scene, navMesh);
        return exportedModel;
    }

    private static void LoadNavMesh(ModelRoot exportedModel, Scene scene, NavMeshFile navMesh)
    {
        if (navMesh.GenerationParams != null)
        {
            for (byte i = 0; i < navMesh.GenerationParams.HullCount; i++)
            {
                var hullAreas = navMesh.GetHullAreas(i);
                if (hullAreas == null)
                {
                    continue;
                }

                var verts = new List<Vector3>();
                var normals = new List<Vector3>();
                var uvs = new List<Vector2>();
                var indices = new List<int>();

                foreach (var area in hullAreas)
                {
                    TriangulateNavMeshPolygon(area.Corners, verts, normals, uvs, indices);
                }

                if (verts.Count == 0)
                {
                    continue;
                }

                var meshName = $"navmesh_hull_{i}";
                var gltfMesh = CreateNavMeshGltfMesh(exportedModel, meshName, verts, normals, uvs, indices,
                    new Vector4(0.25f, 0.13f, 1.0f, 1.0f));

                var node = scene.CreateNode(meshName);
                node.Mesh = gltfMesh;
                node.WorldMatrix = TRANSFORMSOURCETOGLTF;
                node.Extras = new System.Text.Json.Nodes.JsonObject
                {
                    ["HullIndex"] = i,
                };
            }
        }

        if (navMesh.Ladders is { Length: > 0 })
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var indices = new List<int>();

            foreach (var ladder in navMesh.Ladders)
            {
                AddNavMeshLadder(ladder, verts, normals, uvs, indices);
            }

            if (verts.Count > 0)
            {
                var meshName = "navmesh_ladders";
                var gltfMesh = CreateNavMeshGltfMesh(exportedModel, meshName, verts, normals, uvs, indices,
                    new Vector4(0.06f, 1.0f, 0.13f, 1.0f));

                var node = scene.CreateNode(meshName);
                node.Mesh = gltfMesh;
                node.WorldMatrix = TRANSFORMSOURCETOGLTF;
            }
        }
    }

    private static void TriangulateNavMeshPolygon(Vector3[] corners,
        List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices)
    {
        if (corners.Length < 3)
        {
            return;
        }

        // Fan triangulation from the first vertex
        for (var i = 1; i < corners.Length - 1; i++)
        {
            AddTriangleWithNormal(corners[0], corners[i], corners[i + 1], verts, normals, uvs, indices);
        }
    }

    private static void AddNavMeshLadder(NavMeshLadder ladder,
        List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices)
    {
        var normal = ladder.Direction switch
        {
            NavDirectionType.North => new Vector3(0, -1, 0),
            NavDirectionType.East => new Vector3(1, 0, 0),
            NavDirectionType.West => new Vector3(-1, 0, 0),
            _ => new Vector3(0, 1, 0),
        };
        var sidewaysVector = Vector3.Cross(normal, Vector3.UnitZ) * (ladder.Width / 2);

        var bottom1 = ladder.Bottom - sidewaysVector;
        var bottom2 = ladder.Bottom + sidewaysVector;
        var top1 = ladder.Top - sidewaysVector;
        var top2 = ladder.Top + sidewaysVector;

        // Two triangles forming a quad
        AddTriangleWithNormal(bottom2, bottom1, top1, verts, normals, uvs, indices);
        AddTriangleWithNormal(bottom2, top1, top2, verts, normals, uvs, indices);
    }

    private static SharpGLTF.Schema2.Mesh CreateNavMeshGltfMesh(ModelRoot exportedModel, string meshName,
        List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> indices, Vector4 color)
    {
        var mesh = exportedModel.CreateMesh(meshName);
        var primitive = mesh.CreatePrimitive();

        var positionAccessor = CreateAccessor(exportedModel, vertices.ToArray());
        var normalAccessor = CreateAccessor(exportedModel, normals.ToArray());
        var uvAccessor = CreateAccessor(exportedModel, uvs.ToArray());

        primitive.SetVertexAccessor("POSITION", positionAccessor);
        primitive.SetVertexAccessor("NORMAL", normalAccessor);
        primitive.SetVertexAccessor("TEXCOORD_0", uvAccessor);

        primitive.WithIndicesAccessor(PrimitiveType.TRIANGLES, [.. indices]);

        var material = exportedModel.CreateMaterial($"{meshName}_material");
        material.WithPBRMetallicRoughness(color, null, metallicFactor: 0.0f);
        material.Alpha = AlphaMode.OPAQUE;
        material.DoubleSided = true;
        primitive.WithMaterial(material);

        return mesh;
    }
}
