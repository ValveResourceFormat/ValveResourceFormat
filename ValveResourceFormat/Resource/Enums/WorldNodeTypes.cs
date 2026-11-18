namespace ValveResourceFormat
{
    /// <summary>
    /// Worldnode types that resourcecompiler can bucket meshes into when a map is compiled.
    /// </summary>
    /// <remarks>
    /// These types are buckets that resourcecompiler matches meshes into, the order of this enum matches the order the engine tries to bucket
    /// meshes, first bucket the mesh fits into wins.
    /// Nothing here depends on that order.
    /// </remarks>
    public enum WorldNodeTypes
    {
        /// <summary>The name follows none of the compiler's patterns.</summary>
        Unknown,

        /// <summary>Shadow-only meshes in the 3D skybox, named <c>mesh_blocklight_skybox{id}</c>.</summary>
        SkyboxBlockLight,

        /// <summary>Meshes in the 3D skybox, named <c>mesh_skybox</c>.</summary>
        Skybox,

        /// <summary>
        /// Meshes with a material containing the <c>mapbuilder.blocklight</c> attribute, named <c>mesh_blocklight{id}</c> and split into <c>_shadow</c>
        /// and <c>_noshadow</c>.
        /// </summary>
        BlockLight,

        /// <summary>Overlay meshes kept out of render clusters, named <c>mesh_nsoverlay{id}</c>.</summary>
        NoSplitOverlay,

        /// <summary>Overlay meshes, named <c>mesh_overlay{id}</c> or aggregated as <c>agg_overlay</c>.</summary>
        Overlay,

        /// <summary>
        /// Triangles of meshes with world space texture coordinates whose corners face different axes, so the
        /// projection blends across them, named <c>mesh_worldspace_texture_blend</c>. These meshes also carry the
        /// overlay bit, so current compilers put them in <see cref="Overlay"/> first and this class stays empty.
        /// </summary>
        WorldSpaceTexture,

        /// <summary>Meshes with a material containing the <c>mapbuilder.sky</c> attribute, named <c>mesh_skyboxslot_{slot}</c>.</summary>
        SkyboxMaterial,

        /// <summary>
        /// Baked props that are not instanced and whose model has LOD groups, named <c>mesh_prop{id}_{material}</c> or aggregated as
        /// <c>agg_model</c>. Older compilers aggregate them as <c>agg_prop</c>, the name of <see cref="InstancedProp"/>.
        /// </summary>
        BakedPropLod,

        /// <summary>Baked props kept whole, named <c>nosplit{id}_{material}</c> or aggregated as <c>agg_inst</c>.</summary>
        NoSplit,

        /// <summary>
        /// Meshes left out of merging, such as translucent materials or Hammer meshes with Disable Mesh Merging,
        /// named <c>nomerge{id}_{material}</c> or aggregated as <c>agg_nomerge</c>.
        /// </summary>
        NoMerge,

        /// <summary>
        /// Meshes whose shader supports aggregate instancing (<c>SupportsAggregateInstancing</c>), aggregated as <c>agg_merge</c>. The leftovers are
        /// named <c>mesh</c>, or <c>vism{id}_mt_{material}</c> with visibility guided clustering.
        /// </summary>
        Aggregate,

        /// <summary>Every other mesh, named <c>mesh_base</c> (<c>mesh_mat{id}_{material}</c> in older compilers).</summary>
        Base,

        /// <summary>Clutter instances, named <c>clutter{id}_{model}</c>.</summary>
        Clutter,

        /// <summary>
        /// Baked props drawn as instances of their model with a fragment transform per placement, aggregated as <c>agg_prop</c>.
        /// The compiler takes these props before any class when the game enables <c>UseAggregateInstances</c>.
        /// </summary>
        InstancedProp,
    }
}
