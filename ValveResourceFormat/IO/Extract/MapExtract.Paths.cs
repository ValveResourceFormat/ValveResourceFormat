using System.Globalization;
using System.Linq;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.IO;

public sealed partial class MapExtract
{
    private static bool IsCableClass(string className) => className is "cable_static" or "cable_dynamic";

    private static CMapEntity CreateMapEntity(string className, int pathNodeCount)
    {
        if (pathNodeCount == 0)
        {
            return [];
        }

        return IsCableClass(className) ? new CMapCable() : new CMapPath();
    }

    /// <summary>
    /// The keys the compiler flattens a path and its nodes into, which the recovered path objects
    /// carry as their own attributes and children instead.
    /// </summary>
    private static bool IsPathProperty(string key, BaseEntity mapEntity)
    {
        if (mapEntity is CMapPath)
        {
            return key is "closed_loop" or "pathnodes" or "pathnodenames" or "pathnodepinsenabled"
                or "pathnoderadiusscales" or "pathnodecolors" or "pathnodemovespeedtypes";
        }

        return mapEntity is CMapPathNode && key is "path_uniqueid" or "path_index" or "in_tangent_local" or "out_tangent_local";
    }

    private const int LinearPathInterpolation = 0;
    private const int SplinePathInterpolation = 1;
    private const int BezierPathInterpolation = 2;

    private const int LinearTangent = 0;
    private const int SplineTangent = 1;
    private const int DirectionTangent = 2;
    private const int FreeTangent = 3;

    private static float HandleTolerance(float length) => 0.001f + 0.0001f * length;

    private static bool SameHandle(Vector3 a, Vector3 b)
        => Vector3.Distance(a, b) <= HandleTolerance(MathF.Max(a.Length(), b.Length()));

    /// <summary>
    /// Reads back the interpolation Hammer was set to from the handles the compiler derived with it.
    /// Linear and spline handles follow the node positions at a third of their own segment; under
    /// piecewise bezier each node picks its own rule, a direction handle keeps the authored direction
    /// at that length and a free handle is kept as authored, so those come back as per-node tangent types.
    /// </summary>
    private static int DetectPathInterpolation(List<PathParticleRopeNode> nodes, bool closedLoop, out (int In, int Out)[] tangentTypes)
    {
        tangentTypes = new (int, int)[nodes.Count];
        var allLinear = true;
        var allSpline = true;

        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i].Position;
            var previous = i > 0 ? nodes[i - 1].Position : closedLoop ? nodes[^1].Position : node;
            var next = i < nodes.Count - 1 ? nodes[i + 1].Position : closedLoop ? nodes[0].Position : node;

            var inThird = Vector3.Distance(node, previous) / 3f;
            var outThird = Vector3.Distance(next, node) / 3f;

            var inLinear = SameHandle(nodes[i].InTangent, ParticleMath.Normalize(previous - node) * inThird);
            var inSpline = SameHandle(nodes[i].InTangent, ParticleMath.Normalize(previous - next) * inThird);
            var outLinear = SameHandle(nodes[i].OutTangent, ParticleMath.Normalize(next - node) * outThird);
            var outSpline = SameHandle(nodes[i].OutTangent, ParticleMath.Normalize(next - previous) * outThird);

            allLinear &= inLinear && outLinear;
            allSpline &= inSpline && outSpline;

            tangentTypes[i] = (
                TangentType(nodes[i].InTangent, inLinear, inSpline, inThird),
                TangentType(nodes[i].OutTangent, outLinear, outSpline, outThird));
        }

        return allLinear ? LinearPathInterpolation : allSpline ? SplinePathInterpolation : BezierPathInterpolation;
    }

    private static int TangentType(Vector3 handle, bool linear, bool spline, float third)
    {
        if (spline)
        {
            return SplineTangent;
        }

        if (linear)
        {
            return LinearTangent;
        }

        return MathF.Abs(handle.Length() - third) <= HandleTolerance(third) ? DirectionTangent : FreeTangent;
    }

    /// <summary>
    /// The per-node radius scales, or none when the compiler wrote its 0 default for a node class
    /// that declares no radius key.
    /// </summary>
    private static float[] ReadRadiusScales(Entity compiledEntity)
    {
        var radiusScales = PathParticleRope.ParseRadiusScales(compiledEntity.GetStringProperty("pathnoderadiusscales"));
        return radiusScales.All(scale => scale == 0f) ? [] : radiusScales;
    }

    /// <summary>
    /// Parses the <c>pathNodeNames</c> key, one <c>index: name;</c> entry per named node.
    /// </summary>
    private static Dictionary<int, string> ParsePathNodeNames(string? pathNodeNames)
    {
        var names = new Dictionary<int, string>();

        if (string.IsNullOrEmpty(pathNodeNames))
        {
            return names;
        }

        foreach (var entry in pathNodeNames.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = entry.IndexOf(':', StringComparison.Ordinal);

            if (separator > 0 && int.TryParse(entry.AsSpan(0, separator), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            {
                names[index] = entry[(separator + 1)..].Trim();
            }
        }

        return names;
    }

    /// <summary>
    /// Groups the node entities the compiler emits for a path whose node class is not editor-only,
    /// keyed by the hammeruniqueid of the path in this lump they belong to, then by node index.
    /// </summary>
    private static Dictionary<string, Dictionary<int, Entity>> GroupPathNodeEntities(List<Entity> entities)
    {
        var pathIds = new HashSet<string>();
        var nodeEntities = new Dictionary<string, Dictionary<int, Entity>>();

        foreach (var entity in entities)
        {
            if (entity.GetStringProperty("pathnodes") is not null && entity.GetStringProperty("hammeruniqueid") is { } pathId)
            {
                pathIds.Add(pathId);
                continue;
            }

            if (entity.GetStringProperty("path_uniqueid") is { } ownerId && entity.TryGetValue("path_index", out var indexValue)
                && int.TryParse(ToEditString(indexValue), NumberStyles.Integer, CultureInfo.InvariantCulture, out var nodeIndex))
            {
                if (!nodeEntities.TryGetValue(ownerId, out var pathNodes))
                {
                    pathNodes = [];
                    nodeEntities[ownerId] = pathNodes;
                }

                pathNodes[nodeIndex] = entity;
            }
        }

        foreach (var ownerId in nodeEntities.Keys.Where(ownerId => !pathIds.Contains(ownerId)).ToList())
        {
            nodeEntities.Remove(ownerId);
        }

        return nodeEntities;
    }

    /// <summary>
    /// Rebuilds the nodes a path was authored with from the blobs the compiler flattened them into,
    /// placing each one by the path's own world transform, which the blob positions are relative to.
    /// A node the compiler also emitted as its own entity keeps that entity's class, keys and id.
    /// </summary>
    private static void AddPathNodes(CMapPath path, string className, Entity compiledEntity,
        List<PathParticleRopeNode> nodes, Dictionary<int, Entity>? nodeEntities, Matrix4x4 worldTransform)
    {
        var pins = PathParticleRope.ParsePins(compiledEntity.GetStringProperty("pathnodepinsenabled"));
        var radiusScales = ReadRadiusScales(compiledEntity);
        var colors = PathParticleRope.ParseColors(compiledEntity.GetStringProperty("pathnodecolors"));
        var moveSpeedTypes = PathParticleRope.ParseFloatBlob(compiledEntity.GetStringProperty("pathnodemovespeedtypes"));
        var nodeClassName = PathNodeClassName(className);

        path.ClosedLoop = compiledEntity.TryGetValue("closed_loop", out var closedLoop) && ToEditString(closedLoop) is "1" or "true";

        if (nodes.Count >= 3 && nodes[^1].Position == nodes[0].Position
            && nodes[^1].InTangent == nodes[0].InTangent && nodes[^1].OutTangent == nodes[0].OutTangent)
        {
            path.ClosedLoop = true;
            nodes.RemoveAt(nodes.Count - 1);
        }

        var names = ParsePathNodeNames(compiledEntity.GetStringProperty("pathnodenames"));
        path.InterpolationType = DetectPathInterpolation(nodes, path.ClosedLoop, out var tangentTypes);

        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var pathNode = new CMapPathNode();

            if (nodeEntities is not null && nodeEntities.TryGetValue(i, out var nodeEntity))
            {
                AddProperties(nodeEntity.GetStringProperty("classname") ?? nodeClassName, nodeEntity, pathNode);
            }
            else
            {
                pathNode.WithClassName(nodeClassName);

                if (i < pins.Length)
                {
                    pathNode.PinEnabled = pins[i];
                    pathNode.WithProperty("pin_enabled", StringBool(pins[i]));
                }

                if (i < radiusScales.Length)
                {
                    pathNode.RadiusScale = radiusScales[i];
                    pathNode.WithProperty("radius_scale", radiusScales[i].ToString(CultureInfo.InvariantCulture));
                }

                if (i < colors.Length)
                {
                    var color = ToColor(colors[i]);
                    pathNode.TintColor = color;
                    pathNode.WithProperty("color_tint", string.Create(CultureInfo.InvariantCulture, $"{color.R} {color.G} {color.B}"));
                }

                if (i < moveSpeedTypes.Length)
                {
                    pathNode.WithProperty("MoveSpeedType", ((int)moveSpeedTypes[i]).ToString(CultureInfo.InvariantCulture));
                }
            }

            pathNode.Origin = Vector3.Transform(node.Position, worldTransform);
            pathNode.InTangent = Vector3.TransformNormal(node.InTangent, worldTransform);
            pathNode.OutTangent = Vector3.TransformNormal(node.OutTangent, worldTransform);

            if (path.InterpolationType == BezierPathInterpolation)
            {
                pathNode.InTangentType = tangentTypes[i].In;
                pathNode.OutTangentType = tangentTypes[i].Out;
            }

            if (names.TryGetValue(i, out var name))
            {
                pathNode.PathNodeName = name;
                pathNode.WithProperty("node_name", name);
            }

            path.Children.Add(pathNode);
        }
    }

    private static string PathNodeClassName(string pathClassName)
    {
        if (IsCableClass(pathClassName))
        {
            return "path_node_cable";
        }

        return pathClassName switch
        {
            "path_particle_rope" or "path_particle_rope_clientside" => "path_node_particle_rope",
            "map_preview_camera_path" => "map_preview_camera_path_node",
            "dota_movespeed_modifier_path" => "dota_movespeed_path_node",
            _ => "path_node_generic",
        };
    }

    private static Datamodel.Color ToColor(Vector3 color)
    {
        var color32 = Color32.FromVector4Clamped(new Vector4(color, 1f));
        return new Datamodel.Color(color32.R, color32.G, color32.B, color32.A);
    }

    private const float DefaultCableTessellationSpacing = 16f;
    private const float MaxCableTessellationSpacing = 100000f;
    private const float SnapTolerance = 2e-3f;
    private const float HalfPrecisionSnapTolerance = 4e-3f;

    private readonly record struct CableRing(float Along, Vector3 Centre, float Radius, int[] Vertices);

    private readonly record struct RingSeam(float AroundStart, float AroundEnd, bool HasSeam);

    /// <summary>
    /// Reads the tube the compiler built for a cable back into the cable's own attributes: the compiled
    /// map holds them nowhere but in that mesh. Every ring of the tube is one path sample, its vertices run
    /// around the tube once with a seam vertex repeated for the texture, and the texture coordinates carry
    /// the scale, repeats, offsets and orientation the tube was textured with.
    /// </summary>
    private void RecoverCableAttributes(CMapCable cable, string className, Entity compiledEntity, string modelName, List<PathParticleRopeNode> nodes)
    {
        if (className == "cable_dynamic")
        {
            cable.TintColor = ToColor(compiledEntity.GetColor32Property("rendercolor"));
        }

        using var modelResource = FileLoader.LoadFileCompiled(modelName);

        if (modelResource?.DataBlock is not Model model)
        {
            return;
        }

        var physics = model.GetEmbeddedPhys();
        cable.CollisionEnabled = physics is not null || model.GetReferencedPhysNames().Any();

        var embedded = model.GetEmbeddedMeshes().FirstOrDefault();

        if (embedded.Mesh is null || nodes.Count < 2)
        {
            return;
        }

        var vbib = embedded.Mesh.VBIB;

        if (vbib.VertexBuffers.Count == 0 || vbib.IndexBuffers.Count == 0)
        {
            return;
        }

        var vertexBuffer = vbib.VertexBuffers[0];
        var indexBuffer = vbib.IndexBuffers[0];
        var positionField = vertexBuffer.InputLayoutFields.FirstOrDefault(field => field.SemanticName == "POSITION" && field.SemanticIndex == 0);
        var texCoordField = vertexBuffer.InputLayoutFields.FirstOrDefault(field => field.SemanticName == "TEXCOORD" && field.SemanticIndex == 0);

        if (positionField.SemanticName != "POSITION" || texCoordField.SemanticName != "TEXCOORD")
        {
            return;
        }

        var positions = VBIB.GetVector3AttributeArray(vertexBuffer, positionField);
        var texCoords = VBIB.GetVector2AttributeArray(vertexBuffer, texCoordField);
        var halfPrecision = texCoordField.Format == DXGI_FORMAT.R16G16_FLOAT;

        if (positions.Length != texCoords.Length || positions.Length < 8)
        {
            return;
        }

        var indices = GltfModelExporter.ReadIndices(indexBuffer, 0, (int)indexBuffer.ElementCount, 0);

        foreach (var sceneObject in embedded.Mesh.Data.GetArray("m_sceneObjects"))
        {
            foreach (var drawCall in sceneObject.GetArray("m_drawCalls"))
            {
                var material = drawCall.GetStringProperty("m_material") ?? drawCall.GetStringProperty("m_pMaterial");

                if (!string.IsNullOrEmpty(material))
                {
                    cable.MaterialName = material;
                    break;
                }
            }
        }

        var alongIsU = ChooseAlongAxis(positions, texCoords, out var rings);

        if (rings is null || rings.Count < 2)
        {
            return;
        }

        cable.TextureOrientation = alongIsU ? 0 : 1;
        var ringSize = rings[0].Vertices.Length;

        var reversed = IsRingOrderReversed(rings, nodes, cable.ClosedLoop);

        if (reversed)
        {
            rings.Reverse();
        }

        var seam = FindSeam(rings[0], rings[1].Centre - rings[0].Centre, positions, texCoords, alongIsU);
        cable.NumSides = seam.HasSeam ? ringSize - 1 : ringSize;
        var tolerance = halfPrecision ? HalfPrecisionSnapTolerance : SnapTolerance;
        cable.TextureOffsetCircumference = Snap(seam.AroundStart, tolerance);
        cable.TextureRepeatsCircumference = Snap(seam.AroundEnd - seam.AroundStart, tolerance);
        cable.TextureOffsetAlongPath = Snap(rings[0].Along, tolerance);
        cable.FlipFaces = FacesPointInward(positions, indices, rings);

        var radiusScales = ReadRadiusScales(compiledEntity);
        var firstScale = radiusScales.Length > 0 && radiusScales[0] != 0f ? radiusScales[0] : 1f;
        cable.Radius = Snap(rings[0].Radius / firstScale);

        var nodeRings = FindNodeRings(rings, nodes, cable.ClosedLoop);
        cable.TessellationSpacing = RecoverTessellationSpacing(nodes, nodeRings);

        var tubeLength = 0f;

        for (var i = 1; i < rings.Count; i++)
        {
            tubeLength += Vector3.Distance(rings[i].Centre, rings[i - 1].Centre);
        }

        var alongSpan = rings[^1].Along - rings[0].Along;
        var textureSize = LoadCableTextureSize(cable.MaterialName, alongIsU);

        if (tubeLength > 0f && MathF.Abs(alongSpan) > 1e-6f && textureSize > 0)
        {
            cable.TextureScale = Snap(tubeLength / alongSpan / textureSize, tolerance);
        }
        else if (textureSize <= 0)
        {
            ProgressReporter?.Report($"Cable {modelName}: texture size of {cable.MaterialName} is unknown, keeping the default texture scale.");
        }

        if (physics is not null)
        {
            var collisionSides = cable.NumSides;
            var physicsVertices = 0;

            foreach (var part in physics.Parts)
            {
                foreach (var mesh in part.Shape.Meshes)
                {
                    physicsVertices += mesh.Shape.GetVertices().Length;
                }
            }

            var collisionRings = cable.ClosedLoop ? rings.Count - 1 : rings.Count;

            if (physicsVertices == collisionRings * collisionSides)
            {
                cable.PhysicsSimplificationError = 0f;
            }
        }
    }

    /// <summary>
    /// Groups the tube's vertices into rings by whichever texture axis runs along the path: on that axis
    /// the coordinate grows by a fixed amount per unit of distance between the rings, on the other axis the
    /// groups are lines down the tube whose spacing has nothing to do with their coordinate.
    /// </summary>
    private static bool ChooseAlongAxis(Vector3[] positions, Vector2[] texCoords, out List<CableRing>? rings)
    {
        var ringsByU = GroupRings(positions, texCoords, alongIsU: true);
        var ringsByV = GroupRings(positions, texCoords, alongIsU: false);

        var scoreU = SpacingScore(ringsByU) + CircleScore(ringsByU, positions);
        var scoreV = SpacingScore(ringsByV) + CircleScore(ringsByV, positions);

        if (scoreU <= scoreV)
        {
            rings = ringsByU;
            return true;
        }

        rings = ringsByV;
        return false;
    }

    private static float SpacingScore(List<CableRing>? rings)
    {
        if (rings is null)
        {
            return float.MaxValue;
        }

        if (rings.Count < 3)
        {
            return 0f;
        }

        var ratios = new List<float>(rings.Count - 1);

        for (var i = 1; i < rings.Count; i++)
        {
            var chord = Vector3.Distance(rings[i].Centre, rings[i - 1].Centre);
            var step = MathF.Abs(rings[i].Along - rings[i - 1].Along);

            if (step > 1e-6f)
            {
                ratios.Add(chord / step);
            }
        }

        if (ratios.Count < 2)
        {
            return float.MaxValue;
        }

        var mean = ratios.Average();
        return mean > 0f ? ratios.Average(ratio => MathF.Abs(ratio - mean)) / mean : float.MaxValue;
    }

    private static List<CableRing>? GroupRings(Vector3[] positions, Vector2[] texCoords, bool alongIsU)
    {
        var order = Enumerable.Range(0, positions.Length).OrderBy(i => alongIsU ? texCoords[i].X : texCoords[i].Y).ToArray();
        var groups = new List<List<int>>();
        var current = new List<int>();
        var currentValue = float.NaN;

        foreach (var i in order)
        {
            var value = alongIsU ? texCoords[i].X : texCoords[i].Y;

            if (current.Count > 0 && MathF.Abs(value - currentValue) > 1e-4f * MathF.Max(1f, MathF.Abs(currentValue)))
            {
                groups.Add(current);
                current = [];
            }

            if (current.Count == 0)
            {
                currentValue = value;
            }

            current.Add(i);
        }

        if (current.Count > 0)
        {
            groups.Add(current);
        }

        if (groups.Count < 2 || groups.Any(group => group.Count != groups[0].Count) || groups[0].Count < 3)
        {
            return null;
        }

        var rings = new List<CableRing>(groups.Count);

        foreach (var group in groups)
        {
            var distinct = DistinctPositions(group, positions);
            var centre = Vector3.Zero;

            foreach (var i in distinct)
            {
                centre += positions[i];
            }

            centre /= distinct.Count;
            var radius = distinct.Average(i => Vector3.Distance(positions[i], centre));

            rings.Add(new CableRing(alongIsU ? texCoords[group[0]].X : texCoords[group[0]].Y, centre, radius, [.. group]));
        }

        return rings;
    }

    private static List<int> DistinctPositions(List<int> vertices, Vector3[] positions)
    {
        var distinct = new List<int>(vertices.Count);

        foreach (var i in vertices)
        {
            if (!distinct.Any(j => Vector3.DistanceSquared(positions[i], positions[j]) < 1e-6f))
            {
                distinct.Add(i);
            }
        }

        return distinct;
    }

    private static float CircleScore(List<CableRing>? rings, Vector3[] positions)
    {
        if (rings is null)
        {
            return float.MaxValue;
        }

        var score = 0f;

        foreach (var ring in rings)
        {
            if (ring.Radius <= 1e-6f)
            {
                continue;
            }

            var deviation = ring.Vertices.Average(i => MathF.Abs(Vector3.Distance(positions[i], ring.Centre) - ring.Radius));
            score += deviation / ring.Radius;
        }

        return score / rings.Count;
    }

    /// <summary>
    /// A negative texture scale makes the coordinate fall along the path, which puts the rings in the
    /// wrong order when sorted by it: the first ring must sit on node 0 and, on a closed loop where both
    /// ends do, leave it along node 0's outgoing handle.
    /// </summary>
    private static bool IsRingOrderReversed(List<CableRing> rings, List<PathParticleRopeNode> nodes, bool closedLoop)
    {
        if (closedLoop && rings.Count > 2 && nodes[0].OutTangent.LengthSquared() > 0f)
        {
            return Vector3.Dot(rings[1].Centre - rings[0].Centre, nodes[0].OutTangent) < 0f;
        }

        return Vector3.Distance(rings[0].Centre, nodes[0].Position) > Vector3.Distance(rings[^1].Centre, nodes[0].Position);
    }

    /// <summary>
    /// Finds the ring's seam, the position two vertices share, and reads the texture coordinate around
    /// the tube at both of them: the tube's vertices run clockwise about its direction, so the seam vertex
    /// that the next vertex clockwise continues from is where the coordinate starts.
    /// </summary>
    private static RingSeam FindSeam(CableRing ring, Vector3 tangent, Vector3[] positions, Vector2[] texCoords, bool alongIsU)
    {
        float Around(int i) => alongIsU ? texCoords[i].Y : texCoords[i].X;

        var seamA = -1;
        var seamB = -1;

        for (var a = 0; a < ring.Vertices.Length && seamA < 0; a++)
        {
            for (var b = a + 1; b < ring.Vertices.Length; b++)
            {
                if (Vector3.DistanceSquared(positions[ring.Vertices[a]], positions[ring.Vertices[b]]) < 1e-6f)
                {
                    seamA = ring.Vertices[a];
                    seamB = ring.Vertices[b];
                    break;
                }
            }
        }

        if (seamA < 0)
        {
            var around = Around(ring.Vertices[0]);
            return new RingSeam(around, around, HasSeam: false);
        }

        var sides = ring.Vertices.Length - 1;
        var axisA = Vector3.Normalize(positions[seamA] - ring.Centre);
        var axisB = Vector3.Cross(Vector3.Normalize(tangent), axisA);
        var nextClockwise = -1;
        var nextAngle = float.MinValue;

        foreach (var i in ring.Vertices)
        {
            if (i == seamA || i == seamB)
            {
                continue;
            }

            var radial = Vector3.Normalize(positions[i] - ring.Centre);
            var angle = MathF.Atan2(Vector3.Dot(radial, axisB), Vector3.Dot(radial, axisA));

            if (angle < 0f && angle > nextAngle)
            {
                nextAngle = angle;
                nextClockwise = i;
            }
        }

        var valueA = Around(seamA);
        var valueB = Around(seamB);

        if (nextClockwise >= 0)
        {
            var step = Around(nextClockwise);
            var fromA = MathF.Abs(valueA + (step - valueA) * sides - valueB);
            var fromB = MathF.Abs(valueB + (step - valueB) * sides - valueA);

            if (fromB < fromA)
            {
                (seamA, seamB) = (seamB, seamA);
                (valueA, valueB) = (valueB, valueA);
            }
        }

        return new RingSeam(valueA, valueB, HasSeam: true);
    }

    private static bool FacesPointInward(Vector3[] positions, int[] indices, List<CableRing> rings)
    {
        var ringOfVertex = new int[positions.Length];

        for (var r = 0; r < rings.Count; r++)
        {
            foreach (var vertex in rings[r].Vertices)
            {
                ringOfVertex[vertex] = r;
            }
        }

        var inward = 0;
        var outward = 0;

        for (var t = 0; t + 2 < indices.Length; t += 3)
        {
            var a = positions[indices[t]];
            var b = positions[indices[t + 1]];
            var c = positions[indices[t + 2]];
            var faceNormal = Vector3.Cross(b - a, c - a);
            var centroid = (a + b + c) / 3f;
            var dot = Vector3.Dot(faceNormal, centroid - rings[ringOfVertex[indices[t]]].Centre);

            if (dot < 0f)
            {
                inward++;
            }
            else if (dot > 0f)
            {
                outward++;
            }
        }

        return inward > outward;
    }

    /// <summary>
    /// Ring index of every node: the tube starts on node 0 and, on a closed loop, ends on it again, and
    /// the nodes in between sit on the rings nearest to them, in order.
    /// </summary>
    private static int[] FindNodeRings(List<CableRing> rings, List<PathParticleRopeNode> nodes, bool closedLoop)
    {
        var count = closedLoop ? nodes.Count + 1 : nodes.Count;
        var nodeRings = new int[count];
        nodeRings[0] = 0;
        nodeRings[count - 1] = rings.Count - 1;

        for (var k = 1; k < count - 1; k++)
        {
            var target = nodes[k].Position;
            var best = nodeRings[k - 1] + 1;
            var bestDistance = float.MaxValue;

            for (var r = nodeRings[k - 1] + 1; r < rings.Count - (count - 1 - k); r++)
            {
                var distance = Vector3.DistanceSquared(rings[r].Centre, target);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = r;
                }
            }

            nodeRings[k] = best;
        }

        return nodeRings;
    }

    /// <summary>
    /// Every path segment is sampled max(4, ceil(length / spacing)) times, so the sample counts the tube
    /// shows bound the spacing from both sides; the default is kept whenever it reproduces them.
    /// </summary>
    private static float RecoverTessellationSpacing(List<PathParticleRopeNode> nodes, int[] nodeRings)
    {
        var segments = new List<(float Length, int Samples)>();

        for (var k = 0; k + 1 < nodeRings.Length; k++)
        {
            var from = nodes[k % nodes.Count];
            var to = nodes[(k + 1) % nodes.Count];
            var samples = nodeRings[k + 1] - nodeRings[k] + 1;
            var length = CompilerSegmentLength(from.Position, from.OutTangent, to.Position, to.InTangent);

            if (samples >= 2 && length > 0f)
            {
                segments.Add((length, samples));
            }
        }

        if (segments.Count == 0 || Reproduces(DefaultCableTessellationSpacing, segments))
        {
            return DefaultCableTessellationSpacing;
        }

        var low = 0f;
        var high = float.MaxValue;

        foreach (var (length, samples) in segments)
        {
            if (samples > 4)
            {
                low = MathF.Max(low, length / samples);
                high = MathF.Min(high, length / (samples - 1));
            }
            else
            {
                low = MathF.Max(low, length / 4f);
            }
        }

        if (high == float.MaxValue)
        {
            for (var candidate = DefaultCableTessellationSpacing; candidate <= MaxCableTessellationSpacing; candidate *= 2f)
            {
                if (candidate >= low && Reproduces(candidate, segments))
                {
                    return candidate;
                }
            }

            high = low * 1.5f;
        }

        foreach (var unit in new[] { 10f, 5f, 2f, 1f, 0.5f, 0.25f, 0.1f, 0.05f, 0.01f })
        {
            for (var candidate = MathF.Ceiling(low / unit) * unit; candidate < high; candidate += unit)
            {
                if (Reproduces(candidate, segments))
                {
                    return candidate;
                }
            }
        }

        foreach (var fraction in new[] { 0.5f, 0.25f, 0.75f })
        {
            var candidate = low + (high - low) * fraction;

            if (Reproduces(candidate, segments))
            {
                return candidate;
            }
        }

        return (low + high) / 2f;
    }

    private static bool Reproduces(float spacing, List<(float Length, int Samples)> segments)
        => segments.All(segment => Math.Max(4, (int)MathF.Ceiling(segment.Length / spacing)) == segment.Samples);

    /// <summary>
    /// Length of one path segment the way the compiler measures it: the cubic Bezier through the node
    /// handles, summed as chords over 4, 8, ... 128 samples until the sum moves by under one percent.
    /// </summary>
    private static float CompilerSegmentLength(Vector3 p0, Vector3 outHandle, Vector3 p3, Vector3 inHandle)
    {
        var p1 = p0 + outHandle;
        var p2 = p3 + inHandle;
        var c3 = ((3f * p1 - p0) - 3f * p2) + p3;
        var c2 = (3f * p0 - 6f * p1) + 3f * p2;
        var c1 = 3f * p1 - 3f * p0;

        Vector3 Evaluate(float t) => ((((c3 * t) * t) * t) + ((c2 * t) * t) + (c1 * t)) + p0;

        var length = 0f;

        for (var samples = 2; samples <= 128; samples *= 2)
        {
            var previous = length;
            length = 0f;
            var step = 1f / (samples - 1);

            for (var k = 1; k < samples; k++)
            {
                length += Vector3.Distance(Evaluate(k * step), Evaluate((k - 1) * step));
            }

            if (0.01f > MathF.Abs(length - previous) / length)
            {
                break;
            }
        }

        return length;
    }

    /// <summary>
    /// The compiler divides the distance along the path by the width (U along the path) or height
    /// (V along the path) of the material's colour texture.
    /// </summary>
    private int LoadCableTextureSize(string materialName, bool alongIsU)
    {
        if (string.IsNullOrEmpty(materialName))
        {
            return 0;
        }

        using var materialResource = FileLoader.LoadFileCompiled(materialName);

        if (materialResource?.DataBlock is not Material material)
        {
            return 0;
        }

        if (!material.TextureParams.TryGetValue("g_tColor", out var textureName))
        {
            textureName = material.TextureParams.Values.FirstOrDefault();
        }

        if (string.IsNullOrEmpty(textureName))
        {
            return 0;
        }

        using var textureResource = FileLoader.LoadFileCompiled(textureName);

        if (textureResource?.DataBlock is not Texture texture)
        {
            return 0;
        }

        return alongIsU ? texture.Width : texture.Height;
    }

    private static float Snap(float value, float tolerance = SnapTolerance)
    {
        for (var decimals = 2; decimals <= 4; decimals++)
        {
            var rounded = MathF.Round(value, decimals);

            if (MathF.Abs(rounded - value) <= tolerance * MathF.Max(1f, MathF.Abs(value)))
            {
                return rounded;
            }
        }

        return value;
    }
}
