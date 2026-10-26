using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.ContentFormats.HalfEdgeMesh;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests.IO
{
    public class MapDocumentTest
    {
        private static MapDocument Load(CMapRootElement root)
        {
            using var datamodel = new Datamodel.Datamodel("vmap", 29);
            datamodel.Root = root;

            var stream = new MemoryStream();
            datamodel.Save(stream, "binary", 9);
            stream.Position = 0;

            return MapDocument.Load(stream);
        }

        private static CMapEntity MakeEntity(string classname, Vector3 origin)
        {
            var entity = new CMapEntity { Origin = origin };
            entity.WithClassName(classname);
            return entity;
        }

        private static CMapMesh MakeMesh()
        {
            var builder = new HammerMeshBuilder();

            builder.AddVertices([new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0)]);
            builder.AddFace([0, 1, 2, 3], "materials/a.vmat");

            return new CMapMesh { MeshData = builder.GenerateMesh() };
        }

        private static Vector3[] PlacedOrigins(MapDocument document, MapNodeWalker walker)
            => [.. walker.Walk(document).Where(static placedNode => placedNode.Node is CMapEntity).Select(static placedNode => placedNode.Transform.Translation)];

        [Test]
        public async Task LoadsMapSavedByHammer()
        {
            using var document = MapDocument.Load(TestFixtures.Path("hammer_mesh.vmap"));

            var placedNodes = new MapNodeWalker().Walk(document).ToList();
            var mesh = (CMapMesh)placedNodes.Single().Node;
            var geometry = mesh.Mesh.Triangulate();
            var faceCount = mesh.Mesh.FaceHandles.Count();

            using (Assert.Multiple())
            {
                await Assert.That(document.FormatVersion).IsEqualTo(40);
                await Assert.That(faceCount).IsGreaterThan(0);
                await Assert.That(geometry.Sum(static submesh => submesh.Indices.Length)).IsEqualTo((geometry.Sum(static submesh => submesh.Positions.Length) - (2 * faceCount)) * 3);
                await Assert.That(geometry.All(static submesh => submesh.Indices.All(index => (uint)index < (uint)submesh.Positions.Length))).IsTrue();
                await Assert.That(geometry.All(static submesh => submesh.Normals.All(static normal => MathF.Abs(normal.Length() - 1f) < 0.001f))).IsTrue();
                await Assert.That(geometry.All(static submesh => submesh.Material.EndsWith(".vmat", StringComparison.Ordinal))).IsTrue();
            }
        }

        /// <summary>
        /// Node transforms are all in world space, so an instance moves what its target holds by the
        /// difference between itself and the target, and nested instances stack those differences. The
        /// difference is in position and rotation only, the scale of an instance moves nothing.
        /// </summary>
        [Test]
        public async Task InstancesPlaceTheirTargetByTheDifferenceToIt()
        {
            var root = new CMapRootElement();

            var group = new CMapGroup { Origin = new Vector3(10, 0, 0) };
            group.Children.Add(MakeEntity("info_target", new Vector3(12, 0, 5)));

            var instance = new CMapInstance
            {
                Target = group,
                Origin = new Vector3(100, 50, 0),
                Angles = new Datamodel.QAngle(0, 90, 0),
                TintColor = new Datamodel.Color(255, 0, 0, 255),
            };

            var outerGroup = new CMapGroup();
            outerGroup.Children.Add(instance);

            var outerInstance = new CMapInstance
            {
                Target = outerGroup,
                Origin = new Vector3(0, 0, 100),
                Scales = new Vector3(2),
            };

            root.World.Children.Add(group);
            root.World.Children.Add(outerGroup);
            root.World.Children.Add(outerInstance);

            using var document = Load(root);
            var placedNodes = new MapNodeWalker().Walk(document).ToList();

            using (Assert.Multiple())
            {
                await Assert.That(placedNodes.Count).IsEqualTo(3);

                await Assert.That(placedNodes[0].IsInstanced).IsFalse();
                await Assert.That(Vector3.Distance(placedNodes[0].Transform.Translation, new Vector3(12, 0, 5))).IsLessThan(0.001f);

                // Two units ahead of the group becomes two units to the left of the turned instance
                await Assert.That(placedNodes[1].IsInstanced).IsTrue();
                await Assert.That(Vector3.Distance(placedNodes[1].Transform.Translation, new Vector3(100, 52, 5))).IsLessThan(0.001f);
                await Assert.That(Vector3.Distance(placedNodes[1].CreateEntity()!.GetVector3Property("angles"), new Vector3(0, 90, 0))).IsLessThan(0.001f);
                await Assert.That(placedNodes[1].Tint).IsEqualTo(new Vector4(1, 0, 0, 1));

                await Assert.That(Vector3.Distance(placedNodes[2].Transform.Translation, new Vector3(100, 52, 105))).IsLessThan(0.001f);
                await Assert.That(Vector3.Distance(placedNodes[2].CreateEntity()!.GetVector3Property("scales"), Vector3.One)).IsLessThan(0.001f);
            }
        }

        /// <summary>
        /// An instance inside its own target would place itself forever, so it places the target once and
        /// the copy of itself in there places nothing more.
        /// </summary>
        [Test]
        public async Task InstanceInsideItsOwnTargetPlacesItOnce()
        {
            var root = new CMapRootElement();
            var group = new CMapGroup();

            group.Children.Add(MakeEntity("info_target", Vector3.Zero));
            group.Children.Add(new CMapInstance { Target = group });
            root.World.Children.Add(group);

            using var document = Load(root);

            await Assert.That(PlacedOrigins(document, new MapNodeWalker()).Length).IsEqualTo(2);
        }

        [Test]
        public async Task HiddenNodesAreSkipped()
        {
            var root = new CMapRootElement();
            var hidden = MakeEntity("info_target", Vector3.Zero);

            root.World.Children.Add(hidden);
            root.World.Children.Add(MakeEntity("info_target", Vector3.One));
            root.Visibility.Nodes.Add(hidden);
            root.Visibility.HiddenFlags.Add((int)MapNodeHiddenFlags.Hidden);

            using var document = Load(root);
            var visible = PlacedOrigins(document, new MapNodeWalker());

            using (Assert.Multiple())
            {
                await Assert.That(visible.Length).IsEqualTo(1);
                await Assert.That(visible[0]).IsEqualTo(Vector3.One);
                await Assert.That(PlacedOrigins(document, new MapNodeWalker { IncludeHidden = true }).Length).IsEqualTo(2);
            }
        }

        /// <summary>
        /// Hiding a group hides what is in it, in the group itself and in every instance of it, while a
        /// flag outside the hiding ones leaves a node shown.
        /// </summary>
        [Test]
        public async Task HiddenGroupsHideTheirChildrenAndInstances()
        {
            var root = new CMapRootElement();
            var group = new CMapGroup();
            var shown = MakeEntity("info_target", new Vector3(7, 0, 0));

            group.Children.Add(MakeEntity("info_target", Vector3.Zero));
            root.World.Children.Add(group);
            root.World.Children.Add(new CMapInstance { Target = group, Origin = new Vector3(50, 0, 0) });
            root.World.Children.Add(shown);
            root.Visibility.Nodes.Add(group);
            root.Visibility.HiddenFlags.Add((int)MapNodeHiddenFlags.AutoHidden);
            root.Visibility.Nodes.Add(shown);
            root.Visibility.HiddenFlags.Add((int)MapNodeHiddenFlags.Transient);

            using var document = Load(root);
            var visible = PlacedOrigins(document, new MapNodeWalker());

            using (Assert.Multiple())
            {
                await Assert.That(visible.Length).IsEqualTo(1);
                await Assert.That(visible[0]).IsEqualTo(new Vector3(7, 0, 0));
                await Assert.That(PlacedOrigins(document, new MapNodeWalker { IncludeHidden = true }).Length).IsEqualTo(3);
            }
        }

        [Test]
        public async Task PrefabsPlaceTheWorldOfAnotherMap()
        {
            var prefabRoot = new CMapRootElement { IsPrefab = true };
            prefabRoot.World.Children.Add(MakeEntity("info_target", new Vector3(1, 2, 3)));
            prefabRoot.World.Children.Add(new CMapPrefab { TargetMapPath = "maps/prefabs/nested.vmap", LoadIfNested = false });

            var root = new CMapRootElement();
            root.World.Children.Add(new CMapPrefab { TargetMapPath = "maps/prefabs/test.vmap", Origin = new Vector3(10, 0, 0) });

            using var prefab = Load(prefabRoot);
            using var document = Load(root);

            List<string> requested = [];
            var walker = new MapNodeWalker((_, path) =>
            {
                requested.Add(path);
                return prefab;
            });

            var placedNodes = walker.Walk(document).ToList();

            using (Assert.Multiple())
            {
                await Assert.That(string.Join(',', requested)).IsEqualTo("maps/prefabs/test.vmap");
                await Assert.That(placedNodes.Count).IsEqualTo(1);
                await Assert.That(placedNodes[0].Document).IsSameReferenceAs(prefab);
                await Assert.That(placedNodes[0].IsInstanced).IsTrue();
                await Assert.That(placedNodes[0].Transform.Translation).IsEqualTo(new Vector3(11, 2, 3));
                await Assert.That(new MapNodeWalker().Walk(document).Any()).IsFalse();
            }
        }

        [Test]
        public async Task EntitiesSpawnWithLowercaseKeysAndTheirPlacement()
        {
            var root = new CMapRootElement();
            var mapEntity = MakeEntity("prop_static", new Vector3(1.5f, -2, 3));

            mapEntity.Angles = new Datamodel.QAngle(10, 20, 30);
            mapEntity.WithProperty("parentAttachmentName", "hand");
            mapEntity.ConnectionsData.Add(new DmeConnectionData { OutputName = "OnUser1", TargetName = "door", InputName = "Open", Delay = 2f });
            root.World.Children.Add(mapEntity);

            using var document = Load(root);
            var entity = new MapNodeWalker().Walk(document).Single().CreateEntity()!;

            using (Assert.Multiple())
            {
                await Assert.That(entity.GetStringProperty("classname")).IsEqualTo("prop_static");
                await Assert.That(entity.GetStringProperty("parentattachmentname")).IsEqualTo("hand");
                await Assert.That(entity.GetVector3Property("origin")).IsEqualTo(new Vector3(1.5f, -2, 3));
                await Assert.That(entity.GetVector3Property("angles")).IsEqualTo(new Vector3(10, 20, 30));
                await Assert.That(entity.Connections!.Count).IsEqualTo(1);
                await Assert.That(entity.Connections![0].InputName).IsEqualTo("Open");
            }
        }

        /// <summary>
        /// An entity that keeps where it is in its keys is placed by them, and no longer has them once loaded.
        /// Numbers that are missing are zero.
        /// </summary>
        [Test]
        public async Task EntitiesTakeTheirPlacementFromTheirKeys()
        {
            var root = new CMapRootElement();
            var mapEntity = MakeEntity("info_target", new Vector3(100, 100, 100));

            mapEntity.WithProperty("origin", "1 2.5 -3");
            mapEntity.WithProperty("angles", "0 90 0");
            mapEntity.WithProperty("scales", "2 2");
            root.World.Children.Add(mapEntity);

            using var document = Load(root);
            var loaded = (CMapEntity)document.World.Children.Single()!;

            using (Assert.Multiple())
            {
                await Assert.That(loaded.Origin).IsEqualTo(new Vector3(1, 2.5f, -3));
                await Assert.That(loaded.Angles.Yaw).IsEqualTo(90f);
                await Assert.That(loaded.Scales).IsEqualTo(new Vector3(2, 2, 0));
                await Assert.That(loaded.EntityProperties.ContainsKey("origin")).IsFalse();
                await Assert.That(loaded.EntityProperties.ContainsKey("angles")).IsFalse();
                await Assert.That(loaded.EntityProperties.ContainsKey("scales")).IsFalse();
                await Assert.That(loaded.EntityProperties.Get<string>("classname")).IsEqualTo("info_target");
            }
        }

        /// <summary>
        /// Loading converts the saved entity IO of a node into its runtime form and removes the saved elements from
        /// the map. Saving rebuilds them for the write only. Instances and prefabs have entity IO like entities do.
        /// </summary>
        [Test]
        public async Task LoadedNodesLetGoOfTheirStoredEntityIO()
        {
            var root = new CMapRootElement();
            var mapEntity = MakeEntity("logic_relay", Vector3.Zero);
            var prefab = new CMapPrefab { TargetMapPath = "maps/prefabs/test.vmap" };

            mapEntity.ConnectionsData.Add(new DmeConnectionData { OutputName = "OnTrigger", TargetName = "door", InputName = "Open", OverrideParam = "1", Delay = 2f, TimesToFire = 3 });
            mapEntity.RelayPlugData!.Names.Add("plug");
            mapEntity.RelayPlugData.DataTypes.Add(4);
            mapEntity.RelayPlugData.PlugTypes.Add(1);
            mapEntity.RelayPlugData.Descriptions.Add("what it is for");
            prefab.ConnectionsData.Add(new DmeConnectionData { OutputName = "OnSpawn", TargetName = "light", InputName = "TurnOn" });
            root.World.Children.Add(mapEntity);
            root.World.Children.Add(prefab);

            using var document = Load(root);
            var owner = document.Root.Owner!;
            var loaded = document.World.Children.OfType<CMapEntity>().Single();
            var loadedPrefab = document.World.Children.OfType<CMapPrefab>().Single();

            using (Assert.Multiple())
            {
                await Assert.That(owner.AllElements.OfType<DmeConnectionData>().Any()).IsFalse();
                await Assert.That(owner.AllElements.OfType<DmePlugList>().Any()).IsFalse();
                await Assert.That(loaded.RelayPlugData).IsNull();
                await Assert.That(loaded.ConnectionsData.Count).IsEqualTo(0);
                await Assert.That(loaded.Connections.Single().InputName).IsEqualTo("Open");
                await Assert.That(loaded.RelayPlugs.Single()).IsEqualTo(new RelayPlug("plug", 4, 1, "what it is for"));
                await Assert.That(loadedPrefab.Connections.Single().OutputName).IsEqualTo("OnSpawn");
            }

            using var saved = new MemoryStream();
            document.Save(saved, "binary", 9);

            using (Assert.Multiple())
            {
                await Assert.That(owner.AllElements.OfType<DmeConnectionData>().Any()).IsFalse();
                await Assert.That(owner.AllElements.OfType<DmePlugList>().Any()).IsFalse();
            }

            saved.Position = 0;
            using var reloaded = Datamodel.Datamodel.Load<CMapRootElement>(saved, Datamodel.Codecs.DeferredMode.Disabled);
            var stored = reloaded.AllElements.OfType<CMapEntity>().Single();
            var connection = (DmeConnectionData)stored.ConnectionsData.Single()!;

            using (Assert.Multiple())
            {
                await Assert.That($"{connection.OutputName} {connection.TargetName} {connection.InputName} {connection.OverrideParam} {connection.Delay} {connection.TimesToFire}").IsEqualTo("OnTrigger door Open 1 2 3");
                await Assert.That(stored.RelayPlugData!.Names.Single()).IsEqualTo("plug");
                await Assert.That(stored.RelayPlugData.Descriptions.Single()).IsEqualTo("what it is for");
                await Assert.That(reloaded.AllElements.OfType<CMapPrefab>().Single().ConnectionsData.Count).IsEqualTo(1);
            }
        }

        /// <summary>
        /// The visibility manager only keeps a flag for a node that is under the world, and the flag that marks a
        /// node as hidden for the session is cleared.
        /// </summary>
        [Test]
        public async Task VisibilityManagerKeepsTheNodesOfTheWorld()
        {
            var root = new CMapRootElement();
            var hiddenForTheSession = MakeEntity("info_target", Vector3.Zero);
            var hidden = MakeEntity("info_target", Vector3.UnitX);
            var outsideTheWorld = MakeEntity("info_target", Vector3.UnitY);

            // A node of a class that is not modelled here, with a node of its own under it
            var underUnknown = MakeEntity("info_target", Vector3.UnitZ);
            var unknown = new Datamodel.Element { Name = "cordon", ClassName = "CMapCordon" };
            unknown["children"] = new Datamodel.ElementArray { underUnknown };

            root.World.Children.Add(hiddenForTheSession);
            root.World.Children.Add(hidden);
            root.World.Children.Add(unknown);
            root.Visibility.Nodes.Add(hiddenForTheSession);
            root.Visibility.Nodes.Add(outsideTheWorld);
            root.Visibility.Nodes.Add(hidden);
            root.Visibility.Nodes.Add(unknown);
            root.Visibility.Nodes.Add(underUnknown);
            root.Visibility.HiddenFlags.AddRange([(int)MapNodeHiddenFlags.Transient, (int)MapNodeHiddenFlags.Hidden, (int)MapNodeHiddenFlags.Hidden, (int)MapNodeHiddenFlags.Hidden, (int)MapNodeHiddenFlags.AutoHidden, (int)MapNodeHiddenFlags.HiddenByTool]);

            using var document = Load(root);
            var visibility = document.Root.Visibility;
            var nodes = document.World.Children.OfType<CMapEntity>().ToList();

            using (Assert.Multiple())
            {
                await Assert.That(visibility.Nodes.Count).IsEqualTo(4);
                await Assert.That(string.Join(' ', visibility.HiddenFlags)).IsEqualTo("0 1 1 2");
                await Assert.That(visibility.Nodes[2]!.ClassName).IsEqualTo("CMapCordon");
                await Assert.That(document.World.EnumerateDescendants().Count()).IsEqualTo(3);
                await Assert.That(visibility.GetNodeHiddenFlags(nodes[1])).IsEqualTo(MapNodeHiddenFlags.Hidden);
                await Assert.That(document.IsHidden(nodes[0])).IsFalse();
                await Assert.That(document.IsHidden(nodes[1])).IsTrue();
            }
        }

        /// <summary>
        /// The nodes of a path become keys of the path, in its own space, with handles a third of the way
        /// along each segment.
        /// </summary>
        [Test]
        public async Task PathNodesAreFlattenedIntoThePath()
        {
            var root = new CMapRootElement();
            var path = new CMapPath { Origin = new Vector3(100, 0, 0), InterpolationType = (int)MapPathInterpolationType.Linear };

            path.WithClassName("path_particle_rope");
            path.Children.Add(new CMapPathNode { Origin = new Vector3(100, 0, 0) });
            path.Children.Add(new CMapPathNode { Origin = new Vector3(130, 0, 0), PinEnabled = false, RadiusScale = 2f });
            root.World.Children.Add(path);

            using var document = Load(root);
            var entity = new MapNodeWalker().Walk(document).First().CreateEntity()!;
            var nodes = PathParticleRope.ParseNodes(entity.GetStringProperty("pathnodes"));

            using (Assert.Multiple())
            {
                await Assert.That(nodes.Count).IsEqualTo(2);
                await Assert.That(nodes[0].Position).IsEqualTo(Vector3.Zero);
                await Assert.That(nodes[0].OutTangent).IsEqualTo(new Vector3(10, 0, 0));
                await Assert.That(nodes[1].Position).IsEqualTo(new Vector3(30, 0, 0));
                await Assert.That(nodes[1].InTangent).IsEqualTo(new Vector3(-10, 0, 0));
                await Assert.That(PathParticleRope.ParsePins(entity.GetStringProperty("pathnodepinsenabled"))[1]).IsFalse();
                await Assert.That(PathParticleRope.ParseRadiusScales(entity.GetStringProperty("pathnoderadiusscales"))[1]).IsEqualTo(2f);
            }
        }

        /// <summary>
        /// Loading is passed on by the world, which reaches every node under it itself. Saving is passed on by each
        /// node to its own children.
        /// </summary>
        [Test]
        public async Task NodesHearOfLoadingAndSavingThroughTheTree()
        {
            var entityMesh = MakeMesh();
            var nestedMesh = MakeMesh();
            entityMesh.Children.Add(nestedMesh);

            var entity = MakeEntity("func_brush", Vector3.Zero);
            entity.Children.Add(entityMesh);

            var group = new CMapGroup();
            group.Children.Add(entity);

            var root = new CMapRootElement();
            root.World.Children.Add(group);

            CMapMesh[] meshes = [entityMesh, nestedMesh];

            await Assert.That(root.World.EnumerateDescendants()).IsEquivalentTo(new MapNode[] { group, entity, entityMesh, nestedMesh }, EqualityComparer<MapNode>.Default);

            root.PostLoadFromFile();

            using (Assert.Multiple())
            {
                await Assert.That(meshes.All(static mesh => mesh.MeshData == null)).IsTrue();
                await Assert.That(meshes.All(static mesh => mesh.Mesh.FaceHandles.Count() == 1)).IsTrue();
            }

            root.PreSaveToFile();
            await Assert.That(meshes.All(static mesh => mesh.MeshData is { FaceEdgeIndices.Count: 1 })).IsTrue();

            root.PostSaveToFile();
            await Assert.That(meshes.All(static mesh => mesh.MeshData == null)).IsTrue();
        }

        /// <summary>
        /// A mesh that was stored without tangents has them worked out when it is loaded, and is stored with them
        /// from then on.
        /// </summary>
        [Test]
        public async Task MeshesStoredWithoutTangentsAreGivenThem()
        {
            var mapMesh = MakeMesh();
            var streams = mapMesh.MeshData!.FaceVertexData.Streams;

            streams.Remove(streams.OfType<CDmePolygonMeshDataStream>().Single(static stream => stream.SemanticName == "tangent"));

            mapMesh.PostLoadFromFile();

            var tangents = mapMesh.Mesh.HalfEdgeHandles.Where(static hEdge => hEdge.Face.IsValid).Select(hEdge => mapMesh.Mesh.Tangents[hEdge]).ToList();

            using (Assert.Multiple())
            {
                await Assert.That(tangents.Count).IsEqualTo(4);
                await Assert.That(tangents.All(static tangent => MathF.Abs(tangent.AsVector3().Length() - 1f) < 0.001f && MathF.Abs(tangent.W) == 1f)).IsTrue();
                await Assert.That(mapMesh.Mesh.HasStoredStream(MeshComponentType.FaceVertex, "tangent")).IsTrue();
            }
        }

        /// <summary>
        /// Loading converts the saved geometry of a mesh into its runtime form and removes the saved elements from
        /// the map. Saving rebuilds them for the write only, with every stream the mesh had.
        /// </summary>
        [Test]
        public async Task LoadedMeshesLetGoOfTheirStoredForm()
        {
            var path = TestFixtures.Path("hammer_mesh.vmap");

            // What the file holds, read without the map taking the meshes apart
            using var original = Datamodel.Datamodel.Load<CMapRootElement>(path);
            var originalMesh = original.AllElements.OfType<CMapMesh>().Single().MeshData!;

            using var document = MapDocument.Load(path);
            var mesh = document.Root.Owner!.AllElements.OfType<CMapMesh>().Single();

            using (Assert.Multiple())
            {
                await Assert.That(mesh.MeshData).IsNull();
                await Assert.That(document.Root.Owner!.AllElements.OfType<CDmePolygonMesh>().Any()).IsFalse();
                await Assert.That(document.Root.Owner!.AllElements.OfType<CDmePolygonMeshDataStream>().Any()).IsFalse();
                await Assert.That(mesh.Mesh.FaceHandles.Count()).IsEqualTo(originalMesh.FaceEdgeIndices.Count);
            }

            using var saved = new MemoryStream();
            document.Save(saved, "binary", 9);

            // The rebuilt saved data is gone again once the map is written
            using (Assert.Multiple())
            {
                await Assert.That(mesh.MeshData).IsNull();
                await Assert.That(document.Root.Owner!.AllElements.OfType<CDmePolygonMesh>().Any()).IsFalse();
            }

            saved.Position = 0;
            using var reloaded = Datamodel.Datamodel.Load<CMapRootElement>(saved, Datamodel.Codecs.DeferredMode.Disabled);
            var savedMesh = reloaded.AllElements.OfType<CMapMesh>().Single().MeshData!;

            static string Describe(CDmePolygonMeshDataArray array)
                => string.Join(", ", array.Streams.OfType<CDmePolygonMeshDataStream>().Select(static stream
                    => $"{stream.Name}/{stream.StandardAttributeName}/{stream.DataStateFlags}/{stream.Data?.Count}/{stream.SubdivisionBinding?.ClassName}"));

            // A value is reached through the data index of its vertex, face corner or face, and the order the values
            // themselves are stored in is free
            static string Values<T>(CDmePolygonMeshDataArray array, string name, Datamodel.IntArray dataIndices)
            {
                var data = (IList<T>)array.Streams.OfType<CDmePolygonMeshDataStream>().Single(stream => stream.Name == name).Data!;

                return string.Join(' ', dataIndices.Select(index => data[index]));
            }

            using (Assert.Multiple())
            {
                await Assert.That(savedMesh.EdgeVertexIndices.ToArray()).IsEquivalentTo(originalMesh.EdgeVertexIndices.ToArray(), EqualityComparer<int>.Default);
                await Assert.That(savedMesh.EdgeNextIndices.ToArray()).IsEquivalentTo(originalMesh.EdgeNextIndices.ToArray(), EqualityComparer<int>.Default);
                await Assert.That(savedMesh.EdgeOppositeIndices.ToArray()).IsEquivalentTo(originalMesh.EdgeOppositeIndices.ToArray(), EqualityComparer<int>.Default);
                await Assert.That(savedMesh.EdgeFaceIndices.ToArray()).IsEquivalentTo(originalMesh.EdgeFaceIndices.ToArray(), EqualityComparer<int>.Default);
                await Assert.That(savedMesh.FaceEdgeIndices.ToArray()).IsEquivalentTo(originalMesh.FaceEdgeIndices.ToArray(), EqualityComparer<int>.Default);
                await Assert.That(savedMesh.Materials.ToArray()).IsEquivalentTo(originalMesh.Materials.ToArray(), StringComparer.Ordinal);

                await Assert.That(Describe(savedMesh.VertexData)).IsEqualTo(Describe(originalMesh.VertexData));
                await Assert.That(Describe(savedMesh.FaceVertexData)).IsEqualTo(Describe(originalMesh.FaceVertexData));
                await Assert.That(Describe(savedMesh.EdgeData)).IsEqualTo(Describe(originalMesh.EdgeData));
                await Assert.That(Describe(savedMesh.FaceData)).IsEqualTo(Describe(originalMesh.FaceData));
                await Assert.That(savedMesh.SubdivisionData.SubdivisionLevels.Count).IsEqualTo(originalMesh.SubdivisionData.SubdivisionLevels.Count);

                await Assert.That(Values<Vector3>(savedMesh.VertexData, "position:0", savedMesh.VertexDataIndices)).IsEqualTo(Values<Vector3>(originalMesh.VertexData, "position:0", originalMesh.VertexDataIndices));
                await Assert.That(Values<Vector2>(savedMesh.FaceVertexData, "texcoord:0", savedMesh.EdgeVertexDataIndices)).IsEqualTo(Values<Vector2>(originalMesh.FaceVertexData, "texcoord:0", originalMesh.EdgeVertexDataIndices));
                await Assert.That(Values<Vector3>(savedMesh.FaceVertexData, "normal:0", savedMesh.EdgeVertexDataIndices)).IsEqualTo(Values<Vector3>(originalMesh.FaceVertexData, "normal:0", originalMesh.EdgeVertexDataIndices));
                await Assert.That(Values<int>(savedMesh.FaceData, "materialindex:0", savedMesh.FaceDataIndices)).IsEqualTo(Values<int>(originalMesh.FaceData, "materialindex:0", originalMesh.FaceDataIndices));
            }
        }
    }
}
