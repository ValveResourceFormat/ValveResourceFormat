namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// How a model particle renderer reads its sub-model attribute to pick the mesh groups it draws.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/RenderModelSubModelFieldType_t">RenderModelSubModelFieldType_t</seealso>
    public enum RenderModelSubModelFieldType
    {
        /// <summary>The choice of the body group the body group attribute names.</summary>
        SUBMODEL_AS_BODYGROUP_SUBMODEL = 0,
        /// <summary>The index of a single mesh group, chosen within its body group.</summary>
        SUBMODEL_AS_MESHGROUP_INDEX = 1,
        /// <summary>A mask of mesh groups, one bit per group.</summary>
        SUBMODEL_AS_MESHGROUP_MASK = 2,
        /// <summary>Ignored; the model's default mesh groups draw.</summary>
        SUBMODEL_IGNORED_USE_MODEL_DEFAULT_MESHGROUP_MASK = 3,
    }
}
