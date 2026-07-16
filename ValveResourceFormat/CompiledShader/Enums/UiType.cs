namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// UI control types for shader parameters.
/// </summary>
public enum UiType
{
#pragma warning disable CS1591
    None = 0,
    Slider,
    Color,
    Texture,
    VectorText,
    CheckBox,
    Enum,
    LayerReference,
    HeightBlend,
    ColorCorrection,
    Span,
    AngularSpan,
    HeightCorrection,
    SubsurfaceProfile,
    Direction3D,
#pragma warning restore CS1591
}
