using System.Runtime.InteropServices;

namespace ValveResourceFormat.Renderer.Buffers
{
    /// <summary>
    /// Layout of one scene's tile and depth bin cull masks, plus the view they were built against.
    /// </summary>
    /// <remarks>
    /// Its own buffer rather than part of <see cref="ViewConstants"/> because it is per scene, not per
    /// view: the 3D skybox shares the camera but bins its own lights and probes into its own masks, and
    /// binding a different one of these is the whole of switching between them.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential, Pack = 16)]
    public class LightCullConstants
    {
        /// <summary>Index of the first word of the barn light screen tile mask region.</summary>
        public uint LightTileBase;
        /// <summary>Index of the first word of the barn light depth slice mask region.</summary>
        public uint LightSliceBase;
        /// <summary>Number of 32 bit words each barn light tile and slice mask occupies this frame.</summary>
        public uint LightCullWords;
        /// <summary>Right shift converting a pixel coordinate into a tile coordinate.</summary>
        public uint LightTileShift;

        /// <summary>Number of tile columns.</summary>
        public uint LightTileCols;
        /// <summary>Number of tile rows.</summary>
        public uint LightTileRows;
        /// <summary>Number of depth slices in the slice mask region.</summary>
        public uint LightSliceCount;
        /// <summary>Index of the first word of the env map screen tile mask region.</summary>
        public uint EnvMapTileBase;

        /// <summary>Index of the first word of the env map depth bin mask region.</summary>
        public uint EnvMapBinBase;
        /// <summary>Number of 32 bit words each env map tile and bin mask occupies this frame.</summary>
        public uint EnvMapCullWords;
        /// <summary>
        /// Env map probe slots the shading pass may iterate. Bits past it exist in the last mask word but
        /// name nothing, and the all ones masks binning writes when it is off do not distinguish them.
        /// </summary>
        public uint EnvMapCount;
        /// <summary>Padding to maintain 16-byte struct alignment.</summary>
        public uint _Padding0;

        /// <summary>Logarithmic depth slice mapping applied to camera forward distance: X scale, Y bias.</summary>
        public Vector4 LightDepthSliceParams;

        /// <summary>
        /// Maps this pass's pixel to the pixel the same ray occupies in the view the masks were built for:
        /// xy scale, zw bias. Identity for the view that produced them.
        /// </summary>
        public Vector4 LightCullPixelRemap = ViewConstants.PixelRemapIdentity;

        /// <summary>World-space position of the camera the masks were built for.</summary>
        public Vector3 LightCullCameraPosition;
        /// <summary>Padding to maintain 16-byte struct alignment.</summary>
        public float _Padding1;

        /// <summary>World-space forward direction of the camera the masks were built for.</summary>
        public Vector3 LightCullCameraDir;
        /// <summary>Padding to maintain 16-byte struct alignment.</summary>
        public float _Padding2;
    }
}
