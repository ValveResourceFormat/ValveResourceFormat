using System.IO;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Source1.FileSystem;
using ValveResourceFormat.Source1.Material;
using ValveResourceFormat.Source1.Texture;
using VrfMaterial = ValveResourceFormat.ResourceTypes.Material;

namespace ValveResourceFormat.Renderer.Materials
{
    public partial class MaterialLoader
    {
        private const string Source1MaterialExtension = ".vmt";
        private const string Source1TextureExtension = ".vtf";
        private const string Source1HdrTextureExtension = ".hdr.vtf";

        private RenderTexture? Source1DefaultBlack;

        private static bool IsSource1Material(string name)
            => name.EndsWith(Source1MaterialExtension, StringComparison.OrdinalIgnoreCase);

        private static bool IsSource1Texture(string name)
            => name.EndsWith(Source1TextureExtension, StringComparison.OrdinalIgnoreCase);

        private RenderMaterial LoadSource1Material(string name, Dictionary<string, byte>? shaderArguments)
        {
            var fileSystem = RendererContext.Source1FileSystem;

            if (fileSystem == null)
            {
                RendererContext.Logger.LogWarning("No Source 1 file system to load material {Material}", name);
                return GetErrorMaterial();
            }

            VrfMaterial vrfMaterial;

            try
            {
                var highDynamicRange = RendererContext.Source1HighDynamicRange;
                var vmt = VmtFile.Load(name, fileSystem.OpenFile, symbol => ResolveHdrCondition(symbol, highDynamicRange));
                var baseTextureHasAlpha = BaseTextureHasAlpha(fileSystem, vmt);

                vrfMaterial = Source1MaterialConverter.ToVrfMaterial(vmt, name, baseTextureHasAlpha);

                // The loader falls back to the plain cube map when a high dynamic range one does not exist
                if (highDynamicRange && vrfMaterial.TextureParams.TryGetValue("g_tEnvMap", out var envMapPath))
                {
                    vrfMaterial.TextureParams["g_tEnvMap"] = ToHdrTexturePath(envMapPath);
                }
            }
            catch (Exception e) when (e is InvalidDataException or IOException)
            {
                RendererContext.Logger.LogWarning("Failed to load material {Material}: {Message}", name, e.Message);
                return GetErrorMaterial();
            }

            return CreateSource1Material(vrfMaterial, shaderArguments);
        }

        private static bool? ResolveHdrCondition(string symbol, bool highDynamicRange)
        {
            if (symbol.Equals("hdr", StringComparison.OrdinalIgnoreCase))
            {
                return highDynamicRange;
            }

            return null;
        }

        private static string ToHdrTexturePath(string path)
        {
            if (!path.EndsWith(Source1TextureExtension, StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(Source1HdrTextureExtension, StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            return string.Concat(path.AsSpan(0, path.Length - Source1TextureExtension.Length), Source1HdrTextureExtension);
        }

        private static bool BaseTextureHasAlpha(Source1FileSystem fileSystem, VmtFile vmt)
        {
            var baseTexture = vmt.Values.String("$basetexture");

            if (string.IsNullOrWhiteSpace(baseTexture))
            {
                return true;
            }

            using var stream = fileSystem.OpenFile(VmtFile.NormalizePath(baseTexture, Source1TextureExtension));

            if (stream == null)
            {
                return true;
            }

            try
            {
                var header = new VtfFile(stream, onlyHeader: true).Header;
                return (header.Flags & (VtfFlags.ONEBITALPHA | VtfFlags.EIGHTBITALPHA)) != 0;
            }
            catch (Exception e) when (e is InvalidDataException or IOException or NotSupportedException)
            {
                return true;
            }
        }

        /// <summary>
        /// Creates another instance of a Source 1 material, so a mesh can bind textures or set uniforms the other users of the material do not share.
        /// </summary>
        /// <param name="source">The material to copy.</param>
        /// <param name="shaderArguments">The shader combos the copy is created with.</param>
        public RenderMaterial CreateSource1MaterialInstance(RenderMaterial source, Dictionary<string, byte>? shaderArguments)
        {
            ArgumentNullException.ThrowIfNull(source);

            return CreateSource1Material(source.Material, shaderArguments);
        }

        /// <summary>
        /// Creates a copy of a Source 1 material that another Source 1 shader draws, with the same parameters and textures.
        /// </summary>
        /// <param name="source">The material to copy.</param>
        /// <param name="shaderName">The lowercase Source 1 shader name, such as <c>detailsprite</c>.</param>
        public RenderMaterial CreateSource1MaterialWithShader(RenderMaterial source, string shaderName)
        {
            ArgumentNullException.ThrowIfNull(source);

            var vrfMaterial = new VrfMaterial
            {
                Resource = new Resource(ResourceType.Material),
                Name = source.Material.Name,
                ShaderName = Source1MaterialConverter.ShaderPrefix + shaderName,
            };

            foreach (var (name, value) in source.Material.IntParams)
            {
                vrfMaterial.IntParams[name] = value;
            }

            foreach (var (name, value) in source.Material.FloatParams)
            {
                vrfMaterial.FloatParams[name] = value;
            }

            foreach (var (name, value) in source.Material.VectorParams)
            {
                vrfMaterial.VectorParams[name] = value;
            }

            foreach (var (name, value) in source.Material.TextureParams)
            {
                vrfMaterial.TextureParams[name] = value;
            }

            return CreateSource1Material(vrfMaterial, null);
        }

        private RenderMaterial CreateSource1Material(VrfMaterial vrfMaterial, Dictionary<string, byte>? shaderArguments)
        {
            var material = CreateMaterial(vrfMaterial, shaderArguments);
            // Samplers the material does not set read neutral values, so optional features such as the lightmap or a
            // second texture are a no-op instead of showing the error texture
            foreach (var uniformName in material.Shader.UniformNames)
            {
                if (!uniformName.StartsWith("g_t", StringComparison.Ordinal) || material.Textures.ContainsKey(uniformName))
                {
                    continue;
                }

                material.Textures[uniformName] = GetSource1DefaultTexture(uniformName, vrfMaterial);
            }

            return material;
        }

        private RenderTexture GetSource1DefaultTexture(string uniformName, VrfMaterial vrfMaterial)
        {
            if (uniformName == "g_tEnvMap")
            {
                return GetDefaultCube();
            }

            // A surface that only reflects has no color of its own
            if (uniformName == "g_tColor"
                && vrfMaterial.IntParams.GetValueOrDefault("F_ENVMAP") != 0
                && vrfMaterial.IntParams.GetValueOrDefault("F_PHONG") == 0)
            {
                return Source1DefaultBlack ??= CreateSolidTexture(0, 0, 0);
            }

            if (uniformName.Contains("Normal", StringComparison.Ordinal))
            {
                return GetDefaultNormal();
            }

            return GetDefaultColor();
        }

        private RenderTexture LoadSource1Texture(string name, bool srgbRead)
        {
            var fileSystem = RendererContext.Source1FileSystem;
            var stream = fileSystem?.OpenFile(name);

            // A high dynamic range variant of a texture is optional
            if (stream == null && name.EndsWith(Source1HdrTextureExtension, StringComparison.OrdinalIgnoreCase))
            {
                var ldrName = string.Concat(name.AsSpan(0, name.Length - Source1HdrTextureExtension.Length), Source1TextureExtension);
                stream = fileSystem?.OpenFile(ldrName);
            }

            using var textureStream = stream;

            if (textureStream == null)
            {
                RendererContext.Logger.LogWarning("Source 1 texture {Texture} was not found", name);
                return GetErrorTexture();
            }

            try
            {
                var vtf = new VtfFile(textureStream);
                return LoadVtfTexture(vtf, System.IO.Path.GetFileName(name), srgbRead);
            }
            catch (Exception e) when (e is InvalidDataException or IOException or NotSupportedException)
            {
                RendererContext.Logger.LogWarning("Failed to load texture {Texture}: {Message}", name, e.Message);
                return GetErrorTexture();
            }
        }

        /// <summary>Uploads a Source 1 texture, all of its mip levels of the first frame. Six face textures become cube maps.</summary>
        /// <param name="vtf">The parsed texture.</param>
        /// <param name="label">Debug name of the texture.</param>
        /// <param name="srgbRead">Whether to read the color data as sRGB. High dynamic range textures are always linear.</param>
        internal static RenderTexture LoadVtfTexture(VtfFile vtf, string label, bool srgbRead)
        {
            ArgumentNullException.ThrowIfNull(vtf);

            var isCube = vtf.FaceCount >= 6 && (vtf.Flags & VtfFlags.ENVMAP) != 0;
            var target = isCube ? TextureTarget.TextureCubeMap : TextureTarget.Texture2D;
            var isHdr = vtf.IsHighDynamicRange;
            var levels = (vtf.Flags & VtfFlags.NOMIP) != 0 ? 1 : Math.Max(1, vtf.MipmapCount);

            var texture = new RenderTexture(target, vtf.Width, vtf.Height, 1, levels, label)
            {
                Reflectivity = new Vector4(vtf.Reflectivity, 1f),
                SpriteSheetData = vtf.GetSpriteSheetData(),
            };

            SizedInternalFormat internalFormat;
            PixelFormat pixelFormat;
            PixelType pixelType;

            if (isHdr)
            {
                internalFormat = SizedInternalFormat.Rgba32f;
                pixelFormat = PixelFormat.Rgba;
                pixelType = PixelType.Float;
            }
            else
            {
                internalFormat = srgbRead ? SizedInternalFormat.Srgb8Alpha8 : SizedInternalFormat.Rgba8;
                pixelFormat = PixelFormat.Bgra;
                pixelType = PixelType.UnsignedByte;
            }

            GL.TextureStorage2D(texture.Handle, levels, internalFormat, vtf.Width, vtf.Height);

            var faces = isCube ? 6 : 1;

            for (var mip = 0; mip < levels; mip++)
            {
                var width = Math.Max(1, vtf.Width >> mip);
                var height = Math.Max(1, vtf.Height >> mip);

                for (var face = 0; face < faces; face++)
                {
                    using var bitmap = vtf.DecodeMip(mip, 0, face);

                    if (isCube)
                    {
                        GL.TextureSubImage3D(texture.Handle, mip, 0, 0, face, width, height, 1, pixelFormat, pixelType, bitmap.GetPixels());
                    }
                    else
                    {
                        GL.TextureSubImage2D(texture.Handle, mip, 0, 0, width, height, pixelFormat, pixelType, bitmap.GetPixels());
                    }
                }
            }

            var pointSample = (vtf.Flags & VtfFlags.POINTSAMPLE) != 0;

            var minFilter = (pointSample, levels > 1) switch
            {
                (true, true) => TextureMinFilter.NearestMipmapNearest,
                (true, false) => TextureMinFilter.Nearest,
                (false, true) => TextureMinFilter.LinearMipmapLinear,
                _ => TextureMinFilter.Linear,
            };

            texture.SetFiltering(minFilter, pointSample ? TextureMagFilter.Nearest : TextureMagFilter.Linear);

            var clampS = isCube || (vtf.Flags & (VtfFlags.CLAMPS | VtfFlags.BORDER)) != 0 ? RsTextureAddressMode.Clamp : RsTextureAddressMode.Wrap;
            var clampT = isCube || (vtf.Flags & (VtfFlags.CLAMPT | VtfFlags.BORDER)) != 0 ? RsTextureAddressMode.Clamp : RsTextureAddressMode.Wrap;
            texture.SetWrapMode(clampS, clampT, isCube ? RsTextureAddressMode.Clamp : RsTextureAddressMode.Wrap);

            return texture;
        }
    }
}
