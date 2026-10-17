using System.Globalization;
using System.Linq;
using ValveResourceFormat.ResourceTypes.ModelData;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Picks which of the model's meshes are drawn: the active mesh groups, and the LoD level the
    /// <see cref="ModelLodSelector"/> chooses from the on-screen size of the model.
    /// </summary>
    public partial class ModelSceneNode
    {
        private HashSet<string> activeMeshGroups = [];

        private readonly ModelMeshGroups meshGroups;

        private readonly ModelLodSelector lod;

        private readonly List<ModelMeshReference> referenceMeshes;

#pragma warning disable CA1024 // Use properties where appropriate
        /// <summary>Returns every external reference mesh name and its LoD mask, across all levels.</summary>
        public IEnumerable<ModelMeshReference> GetReferenceMeshes()
            => referenceMeshes;

        /// <summary>Returns all mesh group names defined by this model.</summary>
        public IEnumerable<string> GetMeshGroups()
            => meshGroups.Names;

        /// <summary>Returns the material group (skin) names defined by this model, in skin number order.</summary>
        public IEnumerable<string> GetMaterialGroups()
            => materialGroups.Select(static group => group.Name);

        /// <summary>Returns the set of currently active mesh group names.</summary>
        public ICollection<string> GetActiveMeshGroups()
            => activeMeshGroups;
#pragma warning restore CA1024 // Use properties where appropriate

        /// <summary>
        /// Sets which mesh groups are active, rebuilding the renderable mesh list accordingly.
        /// </summary>
        public void SetActiveMeshGroups(IEnumerable<string> setMeshGroups)
        {
            activeMeshGroups = new HashSet<string>(meshGroups.Names.Intersect(setMeshGroups));
            RebuildRenderableMeshes();
        }

        /// <summary>
        /// Returns the choices of every body group of the model, which it holds as mesh groups, in the order of
        /// those mesh groups.
        /// </summary>
        public IEnumerable<BodyGroupChoice> GetBodyGroupChoices()
            => meshGroups.Names.Select(BodyGroupChoice.Parse).OfType<BodyGroupChoice>();

        /// <summary>Makes a choice the one drawn of its body group, in place of the others of that group.</summary>
        /// <param name="choice">One of <see cref="GetBodyGroupChoices"/>.</param>
        public void SetBodyGroupChoice(BodyGroupChoice choice)
        {
            SetActiveMeshGroups([.. activeMeshGroups
                .Where(meshGroup => BodyGroupChoice.Parse(meshGroup) is not { } active
                    || !active.BodyGroup.Equals(choice.BodyGroup, StringComparison.OrdinalIgnoreCase))
                .Append(choice.MeshGroup)]);
        }

        /// <summary>Gets the LoD level currently being rendered (auto-selected or forced).</summary>
        public int ActiveLod => lod.ActiveLevel;

        /// <summary>Gets whether the LoD level is being chosen automatically by distance, rather than forced.</summary>
        public bool IsAutoLod => lod.IsAuto;

        /// <summary>
        /// Sets the LoD level to display, rebuilding the renderable mesh list accordingly.
        /// Pass <see langword="null"/> to enable automatic distance-based selection.
        /// </summary>
        public void SetOverrideLod(int? level)
        {
            lod.SetOverride(level);
            RebuildRenderableMeshes();
        }

        private void UpdateAutoLod(Camera camera)
        {
            if (lod.Update(ComputeLodMetric(camera)))
            {
                RebuildRenderableMeshes();
            }
        }

        /// <summary>
        /// Computes the LoD metric: <c>100 / on-screen size of a unit sphere at the model origin</c>,
        /// scaled by the node's transform.
        /// </summary>
        private float ComputeLodMetric(Camera camera)
        {
            var distance = MathF.Sqrt(GetCameraDistance(camera));
            var scale = Transform.MaxAxisScale();

            // M22 is the projection's 1/tan(vFov/2) y-scale.
            var unitSphereSize = distance > 0f
                ? camera.WindowSize.Y * camera.ProjectionMatrix.M22 * scale / distance
                : float.MaxValue;

            return unitSphereSize > 0f ? 100f / unitSphereSize : 0f;
        }

        private void RebuildRenderableMeshes()
        {
            RenderableMeshes.Clear();

            foreach (var meshRenderer in meshRenderers)
            {
                if (lod.Contains(meshRenderer.MeshIndex)
                    && meshGroups.IsMeshInAnyGroup(meshRenderer.MeshIndex, activeMeshGroups))
                {
                    RenderableMeshes.Add(meshRenderer);
                }
            }
        }
    }

    /// <summary>A choice of a body group of a model, which the model holds as one of its mesh groups.</summary>
    /// <param name="MeshGroup">The mesh group of the choice.</param>
    /// <param name="BodyGroup">The body group the choice is of.</param>
    /// <param name="Index">The number of the choice in its body group.</param>
    /// <param name="Name">The name of the choice, where the model gives it one.</param>
    public readonly record struct BodyGroupChoice(string MeshGroup, string BodyGroup, int Index, string? Name)
    {
        /// <summary>Reads the choice a mesh group holds from its name.</summary>
        /// <param name="meshGroup">The name of the mesh group.</param>
        /// <returns>The choice, or <see langword="null"/> when the mesh group is not one.</returns>
        public static BodyGroupChoice? Parse(string meshGroup)
        {
            ArgumentNullException.ThrowIfNull(meshGroup);

            // HL:A "<bodygroup>_@<index>"
            // CS2  "<bodygroup>_@<index>_#&<choice name>"
            var joiner = meshGroup.IndexOf("_@", StringComparison.Ordinal);

            if (joiner <= 0)
            {
                return null;
            }

            var rest = meshGroup[(joiner + 2)..];
            var nameJoiner = rest.IndexOf("_#&", StringComparison.Ordinal);
            var indexText = nameJoiner < 0 ? rest : rest[..nameJoiner];

            if (!int.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            {
                return null;
            }

            return new BodyGroupChoice(meshGroup, meshGroup[..joiner], index, nameJoiner < 0 ? null : rest[(nameJoiner + 3)..]);
        }
    }
}
