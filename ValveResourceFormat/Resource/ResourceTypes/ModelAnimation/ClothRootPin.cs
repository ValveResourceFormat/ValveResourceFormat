namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Pins a procedural cloth root bone rigidly to the animated bone it hangs from, for when the cloth is not simulated.
    /// </summary>
    /// <param name="Bone">The index of the cloth root bone.</param>
    /// <param name="Anchor">The index of the animated bone the root follows.</param>
    /// <param name="Offset">The root's bind pose times the inverse of the anchor's model-space bind pose.</param>
    public readonly record struct ClothRootPin(int Bone, int Anchor, Matrix4x4 Offset);
}
