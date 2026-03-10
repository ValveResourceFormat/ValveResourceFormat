namespace ValveResourceFormat
{
    /// <summary>
    /// Flags controlling how a render mesh draw primitive is processed and rendered.
    /// Corresponds to <c>MeshDrawPrimitiveFlags_t</c>.
    /// </summary>
    [Flags]
    public enum RenderMeshDrawPrimitiveFlags
    {
        /// <summary>No flags set.</summary>
        None = 0x0,

        /// <summary>Draw call uses the shadow fast path.</summary>
        UseShadowFastPath = 0x1,

        /// <summary>Mesh uses compressed normal and tangent data.</summary>
        UseCompressedNormalTangent = 0x2,

        /// <summary>Mesh acts as an occluder for occlusion culling.</summary>
        IsOccluder = 0x4,

        /// <summary>The mesh input layout does not match the bound material's expected layout.</summary>
        InputLayoutIsNotMatchedToMaterial = 0x8,

        /// <summary>Mesh carries baked lighting data in its vertex stream.</summary>
        HasBakedLightingFromVertexStream = 0x10,

        /// <summary>Mesh uses baked lighting from a lightmap texture.</summary>
        HasBakedLightingFromLightmap = 0x20,

        /// <summary>Draw call can be batched with other draw calls that use dynamic shader constants.</summary>
        CanBatchWithDynamicShaderConstants = 0x40,

        /// <summary>Draw call is submitted after all other draw calls in the pass.</summary>
        DrawLast = 0x80,

        /// <summary>Mesh has per-instance baked lighting data.</summary>
        HasPerInstanceBakedLightingData = 0x100,
    }
}
