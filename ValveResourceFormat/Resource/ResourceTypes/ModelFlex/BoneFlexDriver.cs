namespace ValveResourceFormat.ResourceTypes.ModelFlex
{
    /// <summary>
    /// The component of a bone's local position that drives a flex controller.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/ModelBoneFlexComponent_t">ModelBoneFlexComponent_t</seealso>
    public enum ModelBoneFlexComponent
    {
        /// <summary>The X component of the local position.</summary>
        Tx = 0,

        /// <summary>The Y component of the local position.</summary>
        Ty = 1,

        /// <summary>The Z component of the local position.</summary>
        Tz = 2,
    }

    /// <summary>
    /// A flex controller driven by one component of a bone's local position. The component is mapped from
    /// <see cref="Min"/> and <see cref="Max"/> to the range 0 to 1 and clamped to it.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/ModelBoneFlexDriverControl_t">ModelBoneFlexDriverControl_t</seealso>
    /// <param name="Component">The component of the bone's local position.</param>
    /// <param name="FlexController">Index of the driven flex controller in <see cref="ResourceTypes.Model.FlexControllers"/>.</param>
    /// <param name="Min">The component value that gives 0.</param>
    /// <param name="Max">The component value that gives 1.</param>
    public sealed record BoneFlexDriverControl(ModelBoneFlexComponent Component, int FlexController, float Min, float Max);

    /// <summary>
    /// The flex controllers that one bone drives.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/ModelBoneFlexDriver_t">ModelBoneFlexDriver_t</seealso>
    /// <param name="Bone">Index of the driving bone in the skeleton.</param>
    /// <param name="Controls">The flex controllers the bone drives.</param>
    public sealed record BoneFlexDriver(int Bone, BoneFlexDriverControl[] Controls);
}
