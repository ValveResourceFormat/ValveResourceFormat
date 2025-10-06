namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Variable source types for shader parameters.
/// </summary>
public enum VfxVariableSourceType
{
#pragma warning disable CS1591
    __SetByArtist__,
    __Attribute__,
    __FeatureToInt__,
    __FeatureToBool__,
    __FeatureToFloat__,
    __RenderStateLiteral__,
    __Expression__,
    __SetByArtistAndExpression__,
    Viewport,
    InvViewportSize,
    TextureDim,
    InvTextureDim,
    TextureDimLog2,
    TextureSheetData,
    ShadingComplexity,
    ShaderIDColor,
    ExternalDescSet,
    MaterialID,
    MotionVectorsMaxDistance,
#pragma warning restore CS1591
}
