using System.Linq;
using System.Threading.Tasks;
using SharpGLTF.Schema2;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes.Hull;
using static ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes.Mesh;
using Mesh = SharpGLTF.Schema2.Mesh;
using VMaterial = ValveResourceFormat.ResourceTypes.Material;

namespace ValveResourceFormat.IO;

/// <summary>
/// Physics mesh export functionality for the GLTF model exporter.
/// Converts Source engine physics data (hulls, meshes, spheres, capsules) into GLTF geometry
/// with proper UV coordinates, materials, and textures for each unique surface property.
/// </summary>
public partial class GltfModelExporter
{
    private void LoadPhysicsMeshes(ModelRoot exportedModel, Scene scene, PhysAggregateData phys, Matrix4x4 transform, string? classname = null)
    {
        var bindPose = phys.BindPose;
        var collisionAttributes = phys.CollisionAttributes;
        var physicsSurfaceNames = phys.SurfacePropertyHashes.Select(StringToken.GetKnownString).ToArray();

        // Group shapes by collision attributes and surface properties using simple loops (like ToDmxMesh material grouping)
        for (var collisionAttrIndex = 0; collisionAttrIndex < collisionAttributes.Count; collisionAttrIndex++)
        {
            for (var surfacePropIndex = 0; surfacePropIndex < physicsSurfaceNames.Length; surfacePropIndex++)
            {
                var combinedVerts = new List<Vector3>();
                var combinedNormals = new List<Vector3>();
                var combinedUvs = new List<Vector2>();
                var combinedIndices = new List<int>();

                // Process all parts and collect shapes with matching collision/surface properties
                for (var p = 0; p < phys.Parts.Length; p++)
                {
                    var shape = phys.Parts[p].Shape;
                    var pose = bindPose.Length == 0 ? Matrix4x4.Identity : bindPose[p];

                    // Process sphere shapes with matching properties
                    foreach (var sphere in shape.Spheres.Where(s => s.CollisionAttributeIndex == collisionAttrIndex && s.SurfacePropertyIndex == surfacePropIndex))
                    {
                        var center = Vector3.Transform(sphere.Shape.Center, pose);
                        var radius = sphere.Shape.Radius;
                        CreateSphereMesh(combinedVerts, combinedNormals, combinedUvs, combinedIndices, center, radius);
                    }

                    // Process capsule shapes with matching properties
                    foreach (var capsule in shape.Capsules.Where(c => c.CollisionAttributeIndex == collisionAttrIndex && c.SurfacePropertyIndex == surfacePropIndex))
                    {
                        var center = capsule.Shape.Center;
                        var start = Vector3.Transform(center[0], pose);
                        var end = Vector3.Transform(center[1], pose);
                        var radius = capsule.Shape.Radius;
                        CreateCapsuleMesh(combinedVerts, combinedNormals, combinedUvs, combinedIndices, start, end, radius);
                    }

                    // Process hull shapes with matching properties
                    foreach (var hull in shape.Hulls.Where(h => h.CollisionAttributeIndex == collisionAttrIndex && h.SurfacePropertyIndex == surfacePropIndex))
                    {
                        var vertexPositions = hull.Shape.GetVertexPositions();
                        var transformedPositions = TransformVertices(vertexPositions, pose);
                        var faces = hull.Shape.GetFaces();
                        var edges = hull.Shape.GetEdges();
                        TriangulateHull(faces, edges, transformedPositions, combinedVerts, combinedNormals, combinedUvs, combinedIndices);
                    }

                    // Process mesh shapes with matching properties
                    foreach (var mesh in shape.Meshes.Where(m => m.CollisionAttributeIndex == collisionAttrIndex && m.SurfacePropertyIndex == surfacePropIndex))
                    {
                        var triangles = mesh.Shape.GetTriangles();
                        var vertices = mesh.Shape.GetVertices();
                        var transformedPositions = TransformVertices(vertices, pose);
                        AddTriangles(triangles, transformedPositions, combinedVerts, combinedNormals, combinedUvs, combinedIndices);
                    }
                }

                // Create single GLTF mesh for all shapes with the same collision/surface properties
                if (combinedVerts.Count > 0)
                {
                    var surfaceProperty = physicsSurfaceNames[surfacePropIndex];

                    string meshName;

                    if (classname != null)
                    {
                        meshName = classname;
                    }
                    else
                    {
                        meshName = GetPhysicsMeshName(collisionAttributes[collisionAttrIndex], surfaceProperty);
                    }

                    var gltfMesh = CreatePhysicsMesh(exportedModel, meshName, combinedVerts, combinedNormals, combinedUvs, combinedIndices,
                        collisionAttributes[collisionAttrIndex], physicsSurfaceNames, surfacePropIndex, classname);
                    var node = scene.CreateNode(meshName);
                    node.Mesh = gltfMesh;
                    node.WorldMatrix = transform * TRANSFORMSOURCETOGLTF;

                    var interactAsStrings = collisionAttributes[collisionAttrIndex].GetArray<string>("m_InteractAsStrings");
                    var interactAsArray = new System.Text.Json.Nodes.JsonArray([.. interactAsStrings]);

                    node.Extras = new System.Text.Json.Nodes.JsonObject
                    {
                        ["SurfaceProperty"] = surfaceProperty,
                        ["InteractAs"] = interactAsArray,
                    };
                }
            }
        }
    }

    /// <summary>
    /// Transforms an array of vertices by the given pose matrix.
    /// </summary>
    private static Vector3[] TransformVertices(Span<Vector3> vertices, Matrix4x4 pose)
    {
        var transformed = new Vector3[vertices.Length];
        for (var i = 0; i < vertices.Length; i++)
        {
            transformed[i] = Vector3.Transform(vertices[i], pose);
        }
        return transformed;
    }

    /// <summary>
    /// Triangulates a convex hull using its face and edge data.
    /// Generates triangle vertices, normals, and UV coordinates using planar projection.
    /// </summary>
    private static void TriangulateHull(Span<Face> faces, Span<HalfEdge> edges, Vector3[] transformedPositions,
        List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices)
    {
        foreach (var face in faces)
        {
            var startEdge = face.Edge;

            for (var edge = edges[startEdge].Next; edge != startEdge;)
            {
                var nextEdge = edges[edge].Next;

                if (nextEdge == startEdge)
                {
                    break;
                }

                var a = transformedPositions[edges[startEdge].Origin];
                var b = transformedPositions[edges[edge].Origin];
                var c = transformedPositions[edges[nextEdge].Origin];

                AddTriangleWithNormal(a, b, c, verts, normals, uvs, indices);

                edge = nextEdge;
            }
        }
    }

    /// <summary>
    /// Adds triangles from a triangle mesh to the vertex data.
    /// Generates normals and UV coordinates for each triangle.
    /// </summary>
    private static void AddTriangles(Span<Triangle> triangles, Vector3[] transformedPositions,
        List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices)
    {
        foreach (var tri in triangles)
        {
            var a = transformedPositions[tri.X];
            var b = transformedPositions[tri.Y];
            var c = transformedPositions[tri.Z];

            AddTriangleWithNormal(a, b, c, verts, normals, uvs, indices);
        }
    }

    /// <summary>
    /// Adds a single triangle to the vertex data with computed normal and planar UV coordinates.
    /// </summary>
    private static void AddTriangleWithNormal(Vector3 a, Vector3 b, Vector3 c,
        List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices)
    {
        var normal = ComputeNormal(a, b, c);
        var baseIndex = verts.Count;

        verts.Add(a);
        verts.Add(b);
        verts.Add(c);

        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);

        // Generate planar UV coordinates using the triangle's normal for projection
        var uvA = GeneratePlanarUV(a, normal);
        var uvB = GeneratePlanarUV(b, normal);
        var uvC = GeneratePlanarUV(c, normal);

        uvs.Add(uvA);
        uvs.Add(uvB);
        uvs.Add(uvC);

        indices.Add(baseIndex);
        indices.Add(baseIndex + 1);
        indices.Add(baseIndex + 2);
    }

    private static string GetPhysicsMeshName(KVObject attributes, string surfacePropertyName)
    {
        var tags = attributes.GetArray<string>("m_InteractAsStrings") ?? attributes.GetArray<string>("m_PhysicsTagStrings");
        var group = attributes.GetStringProperty("m_CollisionGroupString");

        var meshName = "physics_group";
        if (group != null && !group.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            meshName = $"physics_{group}";
        }
        if (tags.Length > 0)
        {
            meshName = $"physics_{string.Join("_", tags)}";
        }

        if (!string.IsNullOrEmpty(surfacePropertyName) && !surfacePropertyName.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return $"{meshName}_{surfacePropertyName}";
        }

        return meshName;
    }

    private static Vector3 ComputeNormal(Vector3 a, Vector3 b, Vector3 c)
    {
        var side1 = b - a;
        var side2 = c - a;
        return Vector3.Normalize(Vector3.Cross(side1, side2));
    }

    /// <summary>
    /// Generates a procedural sphere mesh with proper normals and spherical UV coordinates.
    /// </summary>
    private static void CreateSphereMesh(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices, Vector3 center, float radius)
    {
        const int latitudeSegments = 16;
        const int longitudeSegments = 16;

        // Generate vertices with spherical coordinates
        var sphereVerts = new List<Vector3>();
        var sphereNormals = new List<Vector3>();
        var sphereUVs = new List<Vector2>();

        for (var lat = 0; lat <= latitudeSegments; lat++)
        {
            var theta = lat * MathF.PI / latitudeSegments;
            var sinTheta = MathF.Sin(theta);
            var cosTheta = MathF.Cos(theta);

            for (var lon = 0; lon <= longitudeSegments; lon++)
            {
                var phi = lon * 2 * MathF.PI / longitudeSegments;
                var sinPhi = MathF.Sin(phi);
                var cosPhi = MathF.Cos(phi);

                var x = cosPhi * sinTheta;
                var y = cosTheta;
                var z = sinPhi * sinTheta;

                var normal = new Vector3(x, y, z);
                var position = center + normal * radius;

                // Generate spherical UV coordinates
                var u = (float)lon / longitudeSegments;
                var v = (float)lat / latitudeSegments;

                sphereVerts.Add(position);
                sphereNormals.Add(normal);
                sphereUVs.Add(new Vector2(u, v));
            }
        }

        // Generate triangle indices for sphere quads
        var baseIndex = verts.Count;
        verts.AddRange(sphereVerts);
        normals.AddRange(sphereNormals);
        uvs.AddRange(sphereUVs);

        for (var lat = 0; lat < latitudeSegments; lat++)
        {
            for (var lon = 0; lon < longitudeSegments; lon++)
            {
                var first = lat * (longitudeSegments + 1) + lon;
                var second = first + longitudeSegments + 1;

                // First triangle
                indices.Add(baseIndex + first);
                indices.Add(baseIndex + second);
                indices.Add(baseIndex + first + 1);

                // Second triangle
                indices.Add(baseIndex + second);
                indices.Add(baseIndex + second + 1);
                indices.Add(baseIndex + first + 1);
            }
        }
    }

    /// <summary>
    /// Generates a procedural capsule mesh as a cylinder with hemisphere end caps.
    /// Uses cylindrical UV coordinates for the main body.
    /// </summary>
    private static void CreateCapsuleMesh(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices, Vector3 start, Vector3 end, float radius)
    {
        // Create a capsule as a cylinder with hemisphere caps
        var direction = Vector3.Normalize(end - start);
        var length = Vector3.Distance(start, end);
        var center = (start + end) * 0.5f;

        // Find perpendicular vectors
        Vector3 right, up;
        if (Math.Abs(direction.Y) < 0.9f)
        {
            right = Vector3.Normalize(Vector3.Cross(direction, Vector3.UnitY));
            up = Vector3.Cross(right, direction);
        }
        else
        {
            right = Vector3.Normalize(Vector3.Cross(direction, Vector3.UnitX));
            up = Vector3.Cross(right, direction);
        }

        const int segments = 16;
        const int rings = 8;

        var baseIndex = verts.Count;

        // Generate cylinder vertices
        for (var ring = 0; ring <= rings; ring++)
        {
            var t = (float)ring / rings;
            var y = (t - 0.5f) * length;
            var pos = center + direction * y;

            for (var seg = 0; seg <= segments; seg++)
            {
                var angle = seg * 2.0f * MathF.PI / segments;
                var x = MathF.Cos(angle) * radius;
                var z = MathF.Sin(angle) * radius;

                var normal = right * x + up * z;
                normal = Vector3.Normalize(normal);

                // Generate cylindrical UV coordinates
                var u = (float)seg / segments;
                var v = t;

                verts.Add(pos + normal * radius);
                normals.Add(normal);
                uvs.Add(new Vector2(u, v));
            }
        }

        // Generate triangle indices for cylindrical surface
        for (var ring = 0; ring < rings; ring++)
        {
            for (var seg = 0; seg < segments; seg++)
            {
                var current = ring * (segments + 1) + seg;
                var next = current + segments + 1;

                // First triangle
                indices.Add(baseIndex + current);
                indices.Add(baseIndex + next);
                indices.Add(baseIndex + current + 1);

                // Second triangle
                indices.Add(baseIndex + next);
                indices.Add(baseIndex + next + 1);
                indices.Add(baseIndex + current + 1);
            }
        }

        // Add hemispherical end caps using sphere mesh generation
        CreateSphereMesh(verts, normals, uvs, indices, start, radius);
        CreateSphereMesh(verts, normals, uvs, indices, end, radius);
    }

    /// <summary>
    /// Generates UV coordinates using planar projection based on the surface normal.
    /// Chooses the best projection plane based on the dominant normal component for optimal texture mapping.
    /// </summary>
    private static Vector2 GeneratePlanarUV(Vector3 position, Vector3 normal)
    {
        // Generate UV coordinates using planar projection based on the surface normal
        // This provides better texture mapping than simple world coordinates

        var absNormal = new Vector3(Math.Abs(normal.X), Math.Abs(normal.Y), Math.Abs(normal.Z));
        const float uvScale = 0.02f; // Scale factor for reasonable texture tiling

        // Choose the best projection plane based on the dominant normal component
        if (absNormal.Y > absNormal.X && absNormal.Y > absNormal.Z)
        {
            // Project onto XZ plane (for horizontal surfaces like floors/ceilings)
            return new Vector2(position.X * uvScale, position.Z * uvScale);
        }
        else if (absNormal.X > absNormal.Z)
        {
            // Project onto YZ plane (for vertical surfaces facing X direction)
            // Use negative Y to ensure texture is right-side up on vertical walls
            return new Vector2(position.Z * uvScale, -position.Y * uvScale);
        }
        else
        {
            // Project onto XY plane (for vertical surfaces facing Z direction)
            // Use negative Y to ensure texture is right-side up on vertical walls
            return new Vector2(position.X * uvScale, -position.Y * uvScale);
        }
    }

    /// <summary>
    /// Creates a GLTF mesh with physics geometry, materials, and textures.
    /// Uses tool textures for collision attributes when available, otherwise falls back to surface property textures.
    /// </summary>
    private Mesh CreatePhysicsMesh(ModelRoot exportedModel, string meshName, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> indices,
        KVObject collisionAttributes, string[] physicsSurfaceNames, int surfacePropertyIndex, string? classname)
    {
        var mesh = exportedModel.CreateMesh(meshName);
        var primitive = mesh.CreatePrimitive();

        // Create vertex buffer
        var positionAccessor = CreateAccessor(exportedModel, vertices.ToArray());
        var normalAccessor = CreateAccessor(exportedModel, normals.ToArray());
        var uvAccessor = CreateAccessor(exportedModel, uvs.ToArray());

        primitive.SetVertexAccessor("POSITION", positionAccessor);
        primitive.SetVertexAccessor("NORMAL", normalAccessor);
        primitive.SetVertexAccessor("TEXCOORD_0", uvAccessor);

        // Create index buffer
        primitive.WithIndicesAccessor(PrimitiveType.TRIANGLES, [.. indices]);

        // Create material and texture for physics geometry visualization
        if (ExportMaterials)
        {
            var material = exportedModel.CreateMaterial($"{meshName}_material");

            // Try to load and use tool material first
            var tags = collisionAttributes.GetArray<string>("m_InteractAsStrings") ?? collisionAttributes.GetArray<string>("m_PhysicsTagStrings");
            var toolTextureName = MapExtract.GetToolTextureShortenedName_ForInteractStrings([.. tags]);
            var usedToolMaterial = false;

            if (classname != null)
            {
                var toolMaterialPath = MapExtract.GetToolTextureForEntity(classname);
                if (!string.IsNullOrEmpty(toolMaterialPath))
                {
                    var materialResource = FileLoader.LoadFileCompiled(toolMaterialPath);
                    if (materialResource?.DataBlock is VMaterial toolMaterial)
                    {
                        GenerateGLTFMaterialFromRenderMaterial(material, toolMaterial, exportedModel, Vector4.One);
                        usedToolMaterial = true;
                    }
                }
            }
            else if (!string.IsNullOrEmpty(toolTextureName) && toolTextureName != "nodraw")
            {
                var toolMaterialPath = $"materials/tools/tools{toolTextureName}.vmat";

                var materialResource = FileLoader.LoadFileCompiled(toolMaterialPath);
                if (materialResource?.DataBlock is VMaterial toolMaterial)
                {
                    GenerateGLTFMaterialFromRenderMaterial(material, toolMaterial, exportedModel, Vector4.One);
                    usedToolMaterial = true;
                }
            }

            // If no tool material was used, fall back to auto-generated textures
            if (!usedToolMaterial)
            {
                // Configure material as opaque with blue-tinted base color for physics visualization
                material.WithPBRMetallicRoughness(new Vector4(0.5f, 0.5f, 1.0f, 1.0f), null, metallicFactor: 0.0f);
                material.Alpha = AlphaMode.OPAQUE;

                // Generate texture based on tool texture name or surface property
                string textureName;
                SharpGLTF.Schema2.Texture? texture = null;

                if (!string.IsNullOrEmpty(toolTextureName) && toolTextureName != "nodraw")
                {
                    // Use tool texture name for fallback auto-generated texture
                    textureName = $"tool_{toolTextureName}";

                    if (!ExportedTextures.TryGetValue(textureName, out texture))
                    {
                        var newImage = CreateNewGLTFImage(exportedModel, textureName);
                        texture = exportedModel.UseTexture(newImage, TextureSampler);
                        texture.Name = newImage.Name;
                        ExportedTextures[textureName] = texture;

                        // Generate auto-generated tool texture using AddPhysicsTexture
                        var toolTexTask = AddPhysicsTexture(newImage, $"TOOL_{toolTextureName.ToUpperInvariant()}");
                        TextureExportingTasks.Add(toolTexTask);
                    }
                }
                else
                {
                    // Use surface property for auto-generated texture
                    var surfaceProperty = physicsSurfaceNames[surfacePropertyIndex];
                    if (!string.IsNullOrEmpty(surfaceProperty))
                    {
                        textureName = $"physics_{surfaceProperty}";

                        if (!ExportedTextures.TryGetValue(textureName, out texture))
                        {
                            var newImage = CreateNewGLTFImage(exportedModel, textureName);
                            texture = exportedModel.UseTexture(newImage, TextureSampler);
                            texture.Name = newImage.Name;
                            ExportedTextures[textureName] = texture;

                            // Generate physics texture using MapAutoPhysTextureGenerator
                            var physTexTask = AddPhysicsTexture(newImage, surfaceProperty);
                            TextureExportingTasks.Add(physTexTask);
                        }
                    }
                }

                // Apply texture to material if we have one
                if (texture != null)
                {
                    material.FindChannel("BaseColor")?.SetTexture(0, texture);
                }
            }

            primitive.WithMaterial(material);
        }

        return mesh;
    }

    /// <summary>
    /// Generates and links a physics texture for the given surface property using MapAutoPhysTextureGenerator.
    /// </summary>
    private async Task AddPhysicsTexture(Image image, string surfaceProperty)
    {
        await Task.Yield();

        using var bitmap = MapAutoPhysTextureGenerator.GenerateTexture(surfaceProperty);
        var pngBytes = TextureExtract.ToPngImage(bitmap);

        await LinkAndSaveImage(image, pngBytes).ConfigureAwait(false);
    }
}
