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
}
