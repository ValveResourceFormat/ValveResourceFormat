namespace ValveResourceFormat.CompiledShader;

public enum VfxVariableSource // this is not a confirmed name for this enum
{
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
}
