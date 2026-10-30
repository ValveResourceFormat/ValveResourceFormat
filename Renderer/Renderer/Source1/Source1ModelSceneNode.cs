using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Renderer.Buffers;
using ValveResourceFormat.Source1.Model;

namespace ValveResourceFormat.Renderer.Source1
{
    /// <summary>
    /// Scene node that renders a Source 1 model: level of detail 0 geometry, skins, body groups and sequence playback.
    /// </summary>
    /// <remarks>
    /// Geometry is uploaded once per model and shared between the nodes of that model. An animated node evaluates
    /// its sequence on the CPU into bone matrices and the vertex shader skins with them. A node that was asked for
    /// flexes morphs the vertices of its meshes on the CPU into vertex buffers of its own while a flex is active.
    /// </remarks>
    internal partial class Source1ModelSceneNode : MeshCollectionNode, ISkinnedSceneNode
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Vertex
        {
            [VertexAttribute(VertexSlot.Position)] public Vector3 Position;
            [VertexAttribute(VertexSlot.Normal)] public Vector3 Normal;
            [VertexAttribute(VertexSlot.Tangent)] public Vector4 Tangent;
            [VertexAttribute(VertexSlot.TexCoord)] public Vector2 TexCoord;
            [VertexAttribute(VertexSlot.BlendIndices, DXGI_FORMAT.R8G8B8A8_UINT)] public uint BoneIndices;
            [VertexAttribute(VertexSlot.BlendWeight)] public Vector4 BoneWeights;
            [VertexAttribute(VertexSlot.Color1)] public Vector4 BakedLight;

            public static readonly VertexInputLayout InputLayout = VertexInputLayout.FromStruct<Vertex>();
        }

        private sealed class MeshGeometry
        {
            public required string Name { get; init; }
            public required VBIB Buffers { get; init; }
            public required AABB Bounds { get; init; }
        }

        private sealed class MeshEntry
        {
            public required Source1Mesh Source { get; init; }
            public required int BodyPart { get; init; }
            public required int SubModel { get; init; }
            public required RenderableMesh Renderable { get; init; }
            public required DrawCall DrawCall { get; init; }
            public required AABB Bounds { get; init; }

            public required Source1Eyeball? Eyeball { get; init; }

            // Copies of the eyeball materials, which hold the eyeball of this mesh, by material index
            public required Dictionary<int, RenderMaterial> EyeMaterials { get; init; }

            public FlexedVertices? Flexed { get; set; }
        }

        private sealed class FlexedVertices
        {
            public required Vertex[] BaseVertices { get; init; }
            public required Vertex[] Vertices { get; init; }
            public required int Buffer { get; init; }
            public required VertexDrawBuffer SharedBuffer { get; init; }
            public bool IsBound { get; set; }
        }

        private readonly record struct FlexTarget(bool IsActive, float Weight, float PairWeight);

        private static readonly ConditionalWeakTable<Source1Model, Dictionary<Source1Mesh, MeshGeometry>> GeometryByModel = [];
        private static readonly ConditionalWeakTable<Source1Model, string?[]> MaterialPathByModel = [];

        private const string SkinningCombo = "D_SKINNING";

        /// <summary>The shader combo that makes a material take its diffuse light from the baked vertex light stream.</summary>
        internal const string BakedLightingCombo = "D_BAKED_LIGHTING";

        private static int bakedGeometryCount;

        /// <summary>Gets the model this node renders.</summary>
        public Source1Model Model { get; }

        /// <summary>Gets the number of skins the model has.</summary>
        public int SkinCount => Math.Max(1, Model.SkinFamilies.Length);

        /// <summary>Gets the skin that is shown.</summary>
        public int Skin { get; private set; }

        /// <summary>Gets whether the node skins its vertices from a pose.</summary>
        public bool IsAnimated => animation != null;

        /// <summary>Gets the sequence stack that drives the pose, or <see langword="null"/> when the node is not animated.</summary>
        public Source1AnimationLayers? Animation => animation;

        /// <summary>Gets the sequence that plays as the base of the pose.</summary>
        public Source1Sequence? Sequence => animation?.BaseSequence;

        /// <summary>Gets or sets the position in the base sequence from 0 to 1. Setting it poses the node at that cycle.</summary>
        public float Cycle
        {
            get => animation?.BaseCycle ?? 0f;
            set
            {
                if (animation == null)
                {
                    return;
                }

                animation.BaseCycle = value;
                poseDirty = true;
            }
        }

        /// <summary>Gets or sets whether the sequence stops advancing.</summary>
        public bool Paused { get; set; }

        /// <summary>Gets or sets the playback rate, where 1 is the speed of the sequence.</summary>
        public float PlaybackSpeed { get; set; } = 1f;

        /// <summary>Gets or sets whether a sequence that is not marked as looping wraps around at its end instead of holding the last pose.</summary>
        public bool Loop { get; set; }

        /// <summary>Gets or sets whether the node moves with the movement the sequence authors, from where it was when this was turned on.</summary>
        public bool RootMotion
        {
            get;
            set
            {
                if (field == value)
                {
                    return;
                }

                field = value;

                if (value)
                {
                    motionOrigin = Transform;
                }
                else
                {
                    Transform = motionOrigin;
                }
            }
        }

        /// <summary>Gets the pose parameter values in their own units, indexed like <see cref="Source1Model.PoseParameters"/>. Call <see cref="MarkPoseChanged"/> after changing one while paused.</summary>
        public float[] PoseParameterValues => animation?.PoseParameterValues ?? [];

        /// <summary>Gets whether the node morphs its meshes with the flex controllers of the model.</summary>
        public bool HasFlexes => flexValues.Length > 0;

        /// <summary>Gets the value of a flex controller.</summary>
        /// <param name="controller">The index of the controller, see <see cref="Source1Model.FlexControllers"/>.</param>
        public float GetFlexController(int controller) => flexValues[Model.FlexControllers[controller].ValueIndex];

        /// <summary>Sets the value of a flex controller. The meshes morph on the next update.</summary>
        /// <param name="controller">The index of the controller, see <see cref="Source1Model.FlexControllers"/>.</param>
        /// <param name="value">The value, within the range of the controller.</param>
        public void SetFlexController(int controller, float value)
        {
            if (!HasFlexes)
            {
                return;
            }

            flexValues[Model.FlexControllers[controller].ValueIndex] = value;
            flexDirty = true;
        }

        /// <summary>Gets the sequences the node can play.</summary>
        public IReadOnlyList<Source1Sequence> PlayableSequences => playableSequences;

        /// <inheritdoc/>
        public int BoneCount => animation == null ? 0 : Model.Bones.Length;

        /// <inheritdoc/>
        public uint TransformSlot { get; set; }

        internal override List<RenderableMesh> AllRenderableMeshes => allMeshes;

        private readonly List<RenderableMesh> allMeshes = [];
        private readonly List<MeshEntry> entries = [];
        private readonly int[] bodyGroupSelection;
        private readonly Func<string, Dictionary<string, byte>?, RenderMaterial> resolveMaterial;
        private readonly Dictionary<int, RenderMaterial> materialCache = [];
        private readonly Source1AnimationLayers? animation;
        private readonly Source1Sequence[] playableSequences = [];
        private readonly bool hasAutoplay;
        private readonly Dictionary<Source1Mesh, Vector3[]>? bakedLighting;
        private readonly Vector3[] localPositions = [];
        private readonly Quaternion[] localRotations = [];
        private readonly Matrix4x4[] boneToModel = [];
        private readonly Matrix4x4[] skinningMatrices = [];
        private readonly float[] flexValues = [];
        private readonly float[] flexWeights = [];
        private readonly FlexTarget[] flexTargets = [];
        private Matrix4x4 motionOrigin = Matrix4x4.Identity;
        private bool poseDirty;
        private bool flexDirty;

        /// <summary>
        /// Creates a Source 1 model node.
        /// </summary>
        /// <param name="scene">The scene this node belongs to.</param>
        /// <param name="model">The model to render.</param>
        /// <param name="resolveMaterial">Loads the material of a path such as <c>materials/models/x.vmt</c> with the shader combos it is given. When <see langword="null"/>, the material loader of the scene is used.</param>
        /// <param name="skin">The skin family to show.</param>
        /// <param name="animated">Whether to load the sequences of the model and skin it on the GPU from the pose they play.</param>
        /// <param name="vertexLighting">The baked vertex lighting of a static prop. It is ignored when it was not baked for the geometry of the model.</param>
        /// <param name="flexes">Whether to let the flex controllers of the model morph its meshes. It is ignored for a model without flexes.</param>
        public Source1ModelSceneNode(Scene scene, Source1Model model, Func<string, Dictionary<string, byte>?, RenderMaterial>? resolveMaterial = null, int skin = 0, bool animated = false, VhvFile? vertexLighting = null, bool flexes = false)
            : base(scene)
        {
            ArgumentNullException.ThrowIfNull(model);

            Model = model;
            bakedLighting = vertexLighting != null ? GetBakedLighting(model, vertexLighting) : null;
            this.resolveMaterial = resolveMaterial ?? LoadMaterialWithLoader;
            bodyGroupSelection = new int[model.BodyParts.Length];
            eyesAttachment = FindEyesAttachment(model);
            Skin = ClampSkin(skin);

            var boneCount = model.Bones.Length;

            // A static prop is drawn in its reference pose without bone setup
            if (animated && !model.IsStaticProp && boneCount > 0 && TryLoadSequences(model, out playableSequences))
            {
                localPositions = new Vector3[boneCount];
                localRotations = new Quaternion[boneCount];
                boneToModel = new Matrix4x4[boneCount];
                skinningMatrices = new Matrix4x4[boneCount];

                // The engine poses a model with sequence 0 when no other sequence is set
                var firstSequence = model.Sequences.FirstOrDefault();
                var baseSequence = firstSequence != null && playableSequences.Contains(firstSequence) ? firstSequence : null;

                animation = new Source1AnimationLayers(model) { ApplyAutoplay = true, BaseSequence = baseSequence };
                hasAutoplay = model.AutoplaySequences.Any();
                EvaluatePose();
            }

            BuildMeshes();
            RebuildRenderableMeshes();
            UpdateBoundingBox();

            if (flexes && HasMeshFlexes(model))
            {
                flexValues = model.CreateDefaultFlexValues();
                flexWeights = new float[model.FlexDescriptions.Length];
                flexTargets = new FlexTarget[GetMaxFlexCount()];
                flexDirty = true;
                hasFacsEyelids = HasFacsEyeballs(model);
            }
        }

        private int GetMaxFlexCount()
        {
            var count = 0;

            foreach (var entry in entries)
            {
                count = Math.Max(count, entry.Source.Flexes.Length);
            }

            return count;
        }

        private static bool HasMeshFlexes(Source1Model model)
        {
            if (model.FlexControllers.Length == 0)
            {
                return false;
            }

            foreach (var part in model.BodyParts)
            {
                foreach (var subModel in part.Models)
                {
                    foreach (var mesh in subModel.Meshes)
                    {
                        if (mesh.Flexes.Length > 0)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static Dictionary<Source1Mesh, Vector3[]>? GetBakedLighting(Source1Model model, VhvFile vertexLighting)
        {
            if (vertexLighting.Checksum != model.Checksum)
            {
                return null;
            }

            var lighting = new Dictionary<Source1Mesh, Vector3[]>();

            foreach (var part in model.BodyParts)
            {
                foreach (var subModel in part.Models)
                {
                    foreach (var mesh in subModel.Meshes)
                    {
                        var colors = vertexLighting.GetVertexLighting(mesh);

                        if (colors == null)
                        {
                            return null;
                        }

                        lighting[mesh] = colors;
                    }
                }
            }

            return lighting;
        }

        private RenderMaterial LoadMaterialWithLoader(string path, Dictionary<string, byte>? shaderArguments)
            => Scene.RendererContext.MaterialLoader.GetMaterial(path, shaderArguments);

        private bool TryLoadSequences(Source1Model model, out Source1Sequence[] sequences)
        {
            try
            {
                sequences = [.. model.Sequences.Where(sequence => IsPlayable(sequence, model))];
                return true;
            }
            catch (Exception e) when (e is InvalidDataException or InvalidOperationException or IOException)
            {
                Scene.RendererContext.Logger.LogWarning("Failed to load sequences: {Message}", e.Message);
                sequences = [];
                return false;
            }
        }

        private static bool IsPlayable(Source1Sequence sequence, Source1Model model)
        {
            if (sequence.Blends.Length == 0)
            {
                return false;
            }

            foreach (var blend in sequence.Blends)
            {
                if (!blend.IsDataAvailable || blend.FrameCount <= 0 || blend.BoneCount != model.Bones.Length)
                {
                    return false;
                }
            }

            return true;
        }

        private Dictionary<Source1Mesh, MeshGeometry> GetGeometry()
        {
            if (!GeometryByModel.TryGetValue(Model, out var geometry))
            {
                geometry = [];
                GeometryByModel.Add(Model, geometry);
            }

            return geometry;
        }

        private void BuildMeshes()
        {
            var modelKey = string.Create(CultureInfo.InvariantCulture, $"source1:{Model.Checksum:X8}:{Model.Bones.Length}");
            var geometry = GetGeometry();

            for (var partIndex = 0; partIndex < Model.BodyParts.Length; partIndex++)
            {
                var part = Model.BodyParts[partIndex];

                for (var subIndex = 0; subIndex < part.Models.Length; subIndex++)
                {
                    var meshes = part.Models[subIndex].Meshes;

                    for (var meshIndex = 0; meshIndex < meshes.Length; meshIndex++)
                    {
                        var source = meshes[meshIndex];

                        MeshGeometry? meshGeometry;

                        if (bakedLighting != null)
                        {
                            var bakedId = ++bakedGeometryCount;
                            var bakedName = string.Create(CultureInfo.InvariantCulture, $"{modelKey}:{partIndex}.{subIndex}.{meshIndex}:baked{bakedId}");
                            meshGeometry = CreateGeometry(bakedName, source, bakedLighting[source]);
                        }
                        else if (!geometry.TryGetValue(source, out meshGeometry))
                        {
                            var name = string.Create(CultureInfo.InvariantCulture, $"{modelKey}:{partIndex}.{subIndex}.{meshIndex}");
                            meshGeometry = CreateGeometry(name, source, null);
                            geometry.Add(source, meshGeometry);
                        }

                        entries.Add(BuildMesh(meshGeometry, partIndex, subIndex, source));
                    }
                }
            }
        }

        private static MeshGeometry CreateGeometry(string name, Source1Mesh source, Vector3[]? bakedLight)
        {
            var vertices = CreateVertices(source, bakedLight);
            var bounds = new AABB();

            for (var i = 0; i < vertices.Length; i++)
            {
                var position = vertices[i].Position;
                bounds = i == 0 ? new AABB(position, position) : bounds.Encapsulate(position);
            }

            return new MeshGeometry
            {
                Name = name,
                Buffers = Vertex.InputLayout.CreateBuffers<Vertex, ushort>(vertices, source.Indices),
                Bounds = bounds,
            };
        }

        private static Vertex[] CreateVertices(Source1Mesh source, Vector3[]? bakedLight)
        {
            var vertices = new Vertex[source.Vertices.Length];

            for (var i = 0; i < vertices.Length; i++)
            {
                var sourceVertex = source.Vertices[i];

                vertices[i] = new Vertex
                {
                    Position = sourceVertex.Position,
                    Normal = sourceVertex.Normal,
                    Tangent = sourceVertex.Tangent,
                    TexCoord = sourceVertex.TexCoord,
                    BoneIndices = PackBoneIndices(sourceVertex),
                    BoneWeights = GetBoneWeights(sourceVertex),
                    BakedLight = bakedLight != null ? new Vector4(bakedLight[i], 1f) : Vector4.Zero,
                };
            }

            return vertices;
        }

        private static uint PackBoneIndices(in Source1Vertex vertex)
        {
            var bone1 = vertex.NumBones > 1 ? vertex.Bone1 : (byte)0;
            var bone2 = vertex.NumBones > 2 ? vertex.Bone2 : (byte)0;

            return vertex.Bone0 | ((uint)bone1 << 8) | ((uint)bone2 << 16);
        }

        private static Vector4 GetBoneWeights(in Source1Vertex vertex)
        {
            if (vertex.NumBones <= 1)
            {
                return new Vector4(1f, 0f, 0f, 0f);
            }

            var weight2 = vertex.NumBones > 2 ? vertex.Weight2 : 0f;

            return new Vector4(vertex.Weight0, vertex.Weight1, weight2, 0f);
        }

        private MeshEntry BuildMesh(MeshGeometry geometry, int partIndex, int subIndex, Source1Mesh source)
        {
            var eyeball = FindEyeball(partIndex, subIndex, source);
            var eyeMaterials = new Dictionary<int, RenderMaterial>();
            var material = GetEntryMaterial(source, eyeball, eyeMaterials);
            var mesh = RenderableMesh.CreateMesh(geometry.Name, material, geometry.Buffers, geometry.Bounds, Scene.RendererContext);
            var drawCall = mesh.DrawCalls.First();

            if (animation != null)
            {
                mesh.ConfigureSkinning(MeshSkinning.FourBones, Model.Bones.Length);
                mesh.SetSkinningActive(true);
            }

            mesh.PlaceDrawCall(drawCall);

            return new MeshEntry
            {
                Source = source,
                BodyPart = partIndex,
                SubModel = subIndex,
                Renderable = mesh,
                DrawCall = drawCall,
                Bounds = geometry.Bounds,
                Eyeball = eyeball,
                EyeMaterials = eyeMaterials,
            };
        }

        private int GetMaterialIndex(Source1Mesh mesh, int skin)
        {
            var families = Model.SkinFamilies;

            if (skin < families.Length && mesh.SkinRefIndex < families[skin].Length)
            {
                return families[skin][mesh.SkinRefIndex];
            }

            return mesh.MaterialIndex;
        }

        private string? FindMaterialPath(int materialIndex)
        {
            if ((uint)materialIndex >= (uint)Model.MaterialNames.Length)
            {
                return null;
            }

            if (!MaterialPathByModel.TryGetValue(Model, out var paths))
            {
                paths = new string?[Model.MaterialNames.Length + 1];
                MaterialPathByModel.Add(Model, paths);
            }

            var cached = paths[materialIndex];

            if (cached != null)
            {
                return cached.Length == 0 ? null : cached;
            }

            var fileSystem = Scene.RendererContext.Source1FileSystem;

            foreach (var path in Model.GetMaterialPaths(materialIndex))
            {
                if (fileSystem != null && !fileSystem.Exists(path))
                {
                    continue;
                }

                paths[materialIndex] = path;
                return path;
            }

            paths[materialIndex] = string.Empty;
            return null;
        }

        private RenderMaterial GetMaterial(int materialIndex)
        {
            if (materialCache.TryGetValue(materialIndex, out var cached))
            {
                return cached;
            }

            RenderMaterial? material = null;
            var path = FindMaterialPath(materialIndex);

            if (path != null)
            {
                material = resolveMaterial(path, GetShaderArguments());
            }

            material ??= Scene.RendererContext.MaterialLoader.GetMaterial(null, null);
            materialCache[materialIndex] = material;
            return material;
        }

        private Dictionary<string, byte>? GetShaderArguments()
        {
            Dictionary<string, byte>? arguments = null;

            if (animation != null)
            {
                arguments = new Dictionary<string, byte> { [SkinningCombo] = (byte)MeshSkinning.FourBones };
            }

            if (bakedLighting != null)
            {
                arguments ??= [];
                arguments[BakedLightingCombo] = 1;
            }

            return arguments;
        }

        // An invalid skin shows the first family
        private int ClampSkin(int skin) => (uint)skin < (uint)SkinCount ? skin : 0;

        /// <summary>Shows a skin family.</summary>
        /// <param name="skin">The index of the skin family.</param>
        public void SetSkin(int skin)
        {
            skin = ClampSkin(skin);

            if (skin == Skin)
            {
                return;
            }

            Skin = skin;

            foreach (var entry in entries)
            {
                entry.DrawCall.SetNewMaterial(GetEntryMaterial(entry.Source, entry.Eyeball, entry.EyeMaterials));
                entry.Renderable.PlaceDrawCall(entry.DrawCall);
            }
        }

        /// <summary>Gets the sub-model that is shown of a body group.</summary>
        /// <param name="bodyPart">The index of the body group.</param>
        public int GetBodyGroup(int bodyPart) => bodyGroupSelection[bodyPart];

        /// <summary>Chooses the sub-model that is shown of a body group.</summary>
        /// <param name="bodyPart">The index of the body group.</param>
        /// <param name="subModel">The index of the sub-model, which an index past the last one hides the group.</param>
        public void SetBodyGroup(int bodyPart, int subModel)
        {
            bodyGroupSelection[bodyPart] = subModel;
            OnBodyChanged();
        }

        /// <summary>Chooses the sub-model of every body group from the body value of an entity.</summary>
        /// <param name="body">The body value, which holds the sub-model of each group as a mixed radix number in the order of <see cref="Source1BodyPart.Base"/>.</param>
        public void SetBody(int body)
        {
            for (var i = 0; i < bodyGroupSelection.Length; i++)
            {
                var part = Model.BodyParts[i];

                if (part.Base <= 0 || part.Models.Length == 0)
                {
                    continue;
                }

                var subModel = (body / part.Base) % part.Models.Length;
                bodyGroupSelection[i] = Math.Max(subModel, 0);
            }

            OnBodyChanged();
        }

        private void OnBodyChanged()
        {
            flexDirty = HasFlexes;
            RebuildRenderableMeshes();
            UpdateBoundingBox();
            Scene.MarkParentOctreeDirty(this);
        }

        /// <summary>Plays a sequence from its start.</summary>
        /// <param name="sequence">The sequence, or <see langword="null"/> to show the reference pose.</param>
        public void PlaySequence(Source1Sequence? sequence)
        {
            if (animation == null)
            {
                return;
            }

            animation.BaseSequence = sequence;
            animation.BaseCycle = 0f;

            // Nodes that are never updated still need the pose when the scene lays out their bones
            EvaluatePose();
            poseDirty = true;
        }

        /// <summary>Plays a sequence by name.</summary>
        /// <param name="sequenceName">The name of the sequence, or <see langword="null"/> to show the reference pose.</param>
        /// <returns>False when the model has no playable sequence of that name.</returns>
        public bool PlaySequence(string? sequenceName)
        {
            if (sequenceName == null)
            {
                PlaySequence((Source1Sequence?)null);
                return true;
            }

            foreach (var sequence in playableSequences)
            {
                if (sequence.Name.Equals(sequenceName, StringComparison.OrdinalIgnoreCase))
                {
                    PlaySequence(sequence);
                    return true;
                }
            }

            return false;
        }

        /// <summary>Plays the sequence of an activity that the game picks most often.</summary>
        /// <param name="activityName">The activity name, such as <c>ACT_IDLE</c>.</param>
        /// <returns>False when the model has no sequence of that activity.</returns>
        public bool PlayActivity(string activityName)
        {
            Source1Sequence? best = null;

            foreach (var sequence in playableSequences)
            {
                if (sequence.IsDelta || !sequence.ActivityName.Equals(activityName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (best == null || sequence.ActivityWeight > best.ActivityWeight)
                {
                    best = sequence;
                }
            }

            if (best == null)
            {
                return false;
            }

            PlaySequence(best);
            return true;
        }

        /// <summary>Sets a pose parameter by name and poses the node again.</summary>
        /// <returns>False when the model has no pose parameter of that name.</returns>
        public bool SetPoseParameter(string name, float value)
        {
            if (animation == null || !animation.SetPoseParameter(name, value))
            {
                return false;
            }

            poseDirty = true;
            return true;
        }

        /// <summary>Poses the node again on the next update, after a change to <see cref="PoseParameterValues"/> or the layers.</summary>
        public void MarkPoseChanged() => poseDirty = true;

        private void RebuildRenderableMeshes()
        {
            RenderableMeshes.Clear();
            allMeshes.Clear();
            eyeEntries.Clear();

            foreach (var entry in entries)
            {
                allMeshes.Add(entry.Renderable);

                if (bodyGroupSelection[entry.BodyPart] == entry.SubModel)
                {
                    RenderableMeshes.Add(entry.Renderable);

                    if (entry.Eyeball != null)
                    {
                        eyeEntries.Add(entry);
                    }
                }
            }
        }

        private void UpdateBoundingBox()
        {
            var first = true;
            var bounds = new AABB();

            foreach (var entry in entries)
            {
                if (bodyGroupSelection[entry.BodyPart] != entry.SubModel)
                {
                    continue;
                }

                bounds = first ? entry.Bounds : bounds.Union(entry.Bounds);
                first = false;
            }

            if (first)
            {
                bounds = new AABB(Model.HullMin, Model.HullMax);
            }
            else if (animation != null)
            {
                var extent = (bounds.Max - bounds.Min) * 0.5f;
                bounds = new AABB(bounds.Min - extent, bounds.Max + extent);
            }

            LocalBoundingBox = bounds;
        }

        /// <inheritdoc/>
        public override void Update(Scene.UpdateContext context)
        {
            TrackEyeTarget(context.Camera.Location);

            var hasEyeMaterials = HasEyeMaterials();
            var eyeTarget = hasEyeMaterials || (flexDirty && hasFacsEyelids) ? GetEyeTarget() : default;

            if (flexDirty)
            {
                ApplyFlexes(eyeTarget);
            }

            if (hasEyeMaterials)
            {
                UpdateEyes(eyeTarget);
            }

            if (animation == null)
            {
                return;
            }

            var sequence = animation.BaseSequence;

            if (!Paused)
            {
                animation.AutoplayTime += context.Timestep;

                if (sequence != null)
                {
                    AdvanceCycle(sequence, context.Timestep);
                }
            }

            var changes = sequence != null || hasAutoplay;

            if (poseDirty || (!Paused && changes))
            {
                EvaluatePose();
                Scene.UpdateSkinningTransforms(this);
                ApplyRootMotion(sequence);
                flexDirty |= hasFacsEyelids;
            }
        }

        private void ApplyFlexes(Vector3 eyeTarget)
        {
            flexDirty = false;

            Model.EvaluateFlexRules(flexValues, flexWeights);
            ApplyEyelids(eyeTarget);

            foreach (var entry in entries)
            {
                if (entry.Source.Flexes.Length > 0 && bodyGroupSelection[entry.BodyPart] == entry.SubModel)
                {
                    ApplyFlexes(entry);
                }
            }
        }

        private void ApplyFlexes(MeshEntry entry)
        {
            var flexes = entry.Source.Flexes;
            var targets = flexTargets.AsSpan(0, flexes.Length);
            var anyActive = false;

            for (var i = 0; i < flexes.Length; i++)
            {
                var isActive = TryGetFlexWeights(flexes[i], out var weight, out var pairWeight);

                targets[i] = new FlexTarget(isActive, weight, pairWeight);
                anyActive |= isActive;
            }

            if (!anyActive)
            {
                SetFlexedBufferBound(entry, false);
                return;
            }

            var flexed = entry.Flexed ??= CreateFlexedVertices(entry);

            Array.Copy(flexed.BaseVertices, flexed.Vertices, flexed.Vertices.Length);

            for (var i = 0; i < flexes.Length; i++)
            {
                var (isActive, weight, pairWeight) = targets[i];

                if (!isActive)
                {
                    continue;
                }

                foreach (var flexVertex in flexes[i].Vertices)
                {
                    var side = flexVertex.Side;
                    var vertexWeight = (weight * (1f - side)) + (pairWeight * side);
                    var normalChange = flexVertex.NormalDelta * vertexWeight;

                    ref var vertex = ref flexed.Vertices[flexVertex.VertexIndex];
                    vertex.Position += flexVertex.Delta * vertexWeight;
                    vertex.Normal += normalChange;
                    vertex.Tangent += new Vector4(normalChange, 0f);
                }
            }

            for (var i = 0; i < flexes.Length; i++)
            {
                if (!targets[i].IsActive)
                {
                    continue;
                }

                foreach (var flexVertex in flexes[i].Vertices)
                {
                    ref var vertex = ref flexed.Vertices[flexVertex.VertexIndex];
                    vertex.Normal = MathUtils.SafeNormalize(vertex.Normal);

                    var tangent = MathUtils.SafeNormalize(new Vector3(vertex.Tangent.X, vertex.Tangent.Y, vertex.Tangent.Z));
                    vertex.Tangent = new Vector4(tangent, vertex.Tangent.W);
                }
            }

            GL.NamedBufferSubData(flexed.Buffer, IntPtr.Zero, flexed.Vertices.Length * Unsafe.SizeOf<Vertex>(), ref MemoryMarshal.GetArrayDataReference(flexed.Vertices));
            SetFlexedBufferBound(entry, true);
        }

        // The weight of a flex target, and of the target of the other side for a stereo flex. A target that is
        // barely applied is skipped
        private bool TryGetFlexWeights(Source1MeshFlex flex, out float weight, out float pairWeight)
        {
            weight = 0f;
            pairWeight = 0f;

            if ((uint)flex.FlexDesc >= (uint)flexWeights.Length || (uint)flex.FlexPair >= (uint)flexWeights.Length)
            {
                return false;
            }

            weight = flex.Ramp(flexWeights[flex.FlexDesc]);
            pairWeight = flex.FlexPair != 0 ? flex.Ramp(flexWeights[flex.FlexPair]) : weight;

            return MathF.Abs(weight) >= 0.001f || MathF.Abs(pairWeight) >= 0.001f;
        }

        private FlexedVertices CreateFlexedVertices(MeshEntry entry)
        {
            var baseVertices = CreateVertices(entry.Source, bakedLighting?[entry.Source]);
            var name = string.Create(CultureInfo.InvariantCulture, $"{entry.Renderable.Name}:flexed");

            return new FlexedVertices
            {
                BaseVertices = baseVertices,
                Vertices = new Vertex[baseVertices.Length],
                Buffer = GraphicsDevice.CreateBuffer<Vertex>(name, baseVertices, BufferUsage.Dynamic),
                SharedBuffer = entry.DrawCall.VertexBuffers[0],
            };
        }

        // The draw call reads the shared vertex buffer of the model until a flex moves its vertices
        private static void SetFlexedBufferBound(MeshEntry entry, bool bound)
        {
            if (entry.Flexed is not { } flexed || flexed.IsBound == bound)
            {
                return;
            }

            flexed.IsBound = bound;
            entry.DrawCall.VertexBuffers[0] = bound ? flexed.SharedBuffer with { Handle = flexed.Buffer } : flexed.SharedBuffer;
            entry.DrawCall.UpdateVertexArrayObject();
        }

        /// <inheritdoc/>
        public override void Delete()
        {
            foreach (var entry in entries)
            {
                if (entry.Flexed is { } flexed)
                {
                    GL.DeleteBuffer(flexed.Buffer);
                    entry.Flexed = null;
                }
            }
        }

        private void AdvanceCycle(Source1Sequence sequence, float timestep)
        {
            var cyclesPerSecond = sequence.GetCyclesPerSecond(animation!.PoseParameterValues) * PlaybackSpeed;
            var cycle = animation.BaseCycle + (timestep * cyclesPerSecond);

            if (cycle >= 1f || cycle < 0f)
            {
                cycle = Loop || sequence.IsLooping ? cycle - MathF.Floor(cycle) : MathUtils.Saturate(cycle);
            }

            animation.BaseCycle = cycle;
        }

        private void ApplyRootMotion(Source1Sequence? sequence)
        {
            if (!RootMotion || sequence?.Animation is not { FrameCount: > 1 } first)
            {
                return;
            }

            var frame = animation!.BaseCycle * (first.FrameCount - 1);
            var (position, yaw) = first.GetMovement(frame);
            var offset = Matrix4x4.CreateRotationZ(float.DegreesToRadians(yaw)) * Matrix4x4.CreateTranslation(position);

            Transform = offset * motionOrigin;
        }

        private void EvaluatePose()
        {
            poseDirty = false;

            animation!.Evaluate(localPositions, localRotations);

            var bones = Model.Bones;

            for (var i = 0; i < bones.Length; i++)
            {
                var local = Matrix4x4.CreateFromQuaternion(localRotations[i]);
                local.Translation = localPositions[i];

                var parent = bones[i].ParentIndex;
                boneToModel[i] = (uint)parent < (uint)i ? local * boneToModel[parent] : local;
                skinningMatrices[i] = bones[i].PoseToBone * boneToModel[i];
            }
        }

        /// <inheritdoc/>
        public void WriteBoneTransforms(Span<OpenTK.Mathematics.Matrix3x4> boneTransforms)
        {
            var count = Math.Min(boneTransforms.Length, skinningMatrices.Length);

            for (var i = 0; i < count; i++)
            {
                boneTransforms[i] = skinningMatrices[i].To3x4();
            }
        }

        /// <inheritdoc/>
        public override IEnumerable<string> GetSupportedRenderModes()
            => allMeshes.SelectMany(static mesh => mesh.GetSupportedRenderModes());

#if DEBUG
        /// <inheritdoc/>
        public override void UpdateVertexArrayObjects()
        {
            foreach (var mesh in allMeshes)
            {
                mesh.UpdateVertexArrayObjects();
            }
        }
#endif
    }
}
