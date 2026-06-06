using System;
using System.Numerics;
using SkiaSharp;
using GUI.Utils;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Viewers
{
    /// <summary>
    /// Loads the CS2 radar overview (image + world-to-pixel calibration) for a map so the
    /// demo HUD minimap can show the real map and place player dots at their true positions.
    /// </summary>
    sealed class RadarOverview : IDisposable
    {
        public SKBitmap Image { get; }

        public float PosX { get; }

        public float PosY { get; }

        public float Scale { get; }

        public int ImageSize { get; }

        private RadarOverview(SKBitmap image, float posX, float posY, float scale)
        {
            Image = image;
            PosX = posX;
            PosY = posY;
            Scale = scale;
            ImageSize = image.Width;
        }

        /// <summary>
        /// Converts a world position to a 0..1 fraction across the radar image.
        /// </summary>
        public (float X, float Y) WorldToFraction(Vector3 world)
        {
            var px = (world.X - PosX) / Scale;
            var py = (PosY - world.Y) / Scale;
            var fx = Math.Clamp(px / ImageSize, 0f, 1f);
            var fy = Math.Clamp(py / ImageSize, 0f, 1f);
            return (fx, fy);
        }

        public void Dispose()
        {
            Image.Dispose();
        }

        public static RadarOverview? TryLoad(VrfGuiContext vrfGuiContext, string mapName)
        {
            return TryLoadForName(vrfGuiContext, mapName) ?? TryLoadForName(vrfGuiContext, TrimTrailingDigits(mapName));
        }

        private static RadarOverview? TryLoadForName(VrfGuiContext vrfGuiContext, string mapName)
        {
            if (string.IsNullOrEmpty(mapName))
            {
                return null;
            }

            try
            {
                if (!TryReadOverviewDefinition(vrfGuiContext, mapName, out var posX, out var posY, out var scale))
                {
                    return null;
                }

                var textureResource = vrfGuiContext.LoadFileCompiled($"panorama/images/overheadmaps/{mapName}_radar_psd.vtex");

                if (textureResource?.DataBlock is not Texture texture)
                {
                    return null;
                }

                var skiaBitmap = texture.GenerateBitmap();

                return new RadarOverview(skiaBitmap, posX, posY, scale);
            }
            catch (Exception e)
            {
                Log.Warn(nameof(RadarOverview), $"Failed to load radar overview for '{mapName}': {e.Message}");
                return null;
            }
        }

        private static bool TryReadOverviewDefinition(VrfGuiContext vrfGuiContext, string mapName, out float posX, out float posY, out float scale)
        {
            posX = posY = scale = 0f;

            using var stream = vrfGuiContext.GetFileStream($"resource/overviews/{mapName}.txt");

            if (stream == null)
            {
                return false;
            }

            var data = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream, KVSerializerOptions.DefaultOptions).Root;

            // Multi-level maps (nuke, vertigo) use verticalsections; not supported yet.
            if (data.TryGetValue("verticalsections", out _))
            {
                return false;
            }

            posX = data.GetFloatProperty("pos_x");
            posY = data.GetFloatProperty("pos_y");
            scale = data.GetFloatProperty("scale");

            return scale != 0f;
        }

        private static string TrimTrailingDigits(string value)
        {
            var end = value.Length;

            while (end > 0 && char.IsDigit(value[end - 1]))
            {
                end--;
            }

            return value[..end];
        }
    }
}
