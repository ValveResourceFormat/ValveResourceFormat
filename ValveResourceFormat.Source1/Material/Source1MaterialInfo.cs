namespace ValveResourceFormat.Source1.Material;

/// <summary>What the conversion of a map needs to know about a material to size the sprites and ropes drawn with it, and to give its collision a surface.</summary>
/// <param name="TextureSize">The size in texels of the base texture.</param>
/// <param name="SpriteOrientation">The <c>$spriteorientation</c> of the material, or null when it sets none.</param>
/// <param name="SpriteOrigin">The <c>$spriteorigin</c> of the material.</param>
/// <param name="DecalScale">The <c>$decalscale</c> of the material, which scales the texture size to the world size of an <c>infodecal</c>.</param>
/// <param name="SurfaceProperty">The <c>$surfaceprop</c> of the material, or null when it sets none.</param>
/// <param name="IsBlended">Whether the converted material blends with what is behind it, so that it draws after the opaque surfaces.</param>
internal sealed record Source1MaterialInfo(Vector2 TextureSize, string? SpriteOrientation, Vector2 SpriteOrigin, float DecalScale, string? SurfaceProperty, bool IsBlended);
