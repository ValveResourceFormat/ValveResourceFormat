using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveResourceFormat;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.IO;
using ValveResourceFormat.Utils;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;

namespace Tests
{
    public class ShaderTest
    {
        public static string ShadersDir
            => Path.Combine(TestContext.TestDirectory!, "Files", "Shaders");

        public static IEnumerable<string> ShaderFiles()
        {
            var files = Directory.GetFiles(ShadersDir, "*.vcs");

            if (files.Length == 0)
            {
                throw new InvalidOperationException($"There are no shaders to test in {ShadersDir}.");
            }

            return [.. files.Select(file => Path.GetFileName(file))];
        }

        [Test]
        [MethodDataSource(nameof(ShaderFiles))]
        public async Task ParseShaders(string shaderFile)
        {
            using var shader = new VfxProgramData();
            shader.Read(Path.Combine(ShadersDir, shaderFile));

            using var sw = new IndentedTextWriter();

            shader.PrintSummary(sw);
            await Assert.That(sw.ToString().Length).IsGreaterThanOrEqualTo(100);

            foreach (var zframe in shader.StaticComboEntries)
            {
                var value = zframe.Value.Unserialize();
                await Assert.That(value).IsNotNull();
                var zframeSummary = new PrintZFrameSummary(value, sw);
            }
        }

        [Test]
        public async Task ShaderResourceDataMatchesBinary()
        {
            using var shader1 = new VfxProgramData();
            using var shader2 = new VfxProgramData();

            shader1.Read(Path.Combine(ShadersDir, "vcs69_bloom_vulkan_40_ps.vcs"));
            shader2.Read(Path.Combine(ShadersDir, "vcs70_resource_bloom_vulkan_40_ps.vcs"));

            using (Assert.Multiple())
            {
                await Assert.That(shader2.VcsProgramType).IsEqualTo(shader1.VcsProgramType);
                await Assert.That(shader2.VcsPlatformType).IsEqualTo(shader1.VcsPlatformType);
                await Assert.That(shader2.VcsShaderModelType).IsEqualTo(shader1.VcsShaderModelType);
                await Assert.That(shader2.FileHash).IsEqualTo(shader1.FileHash);
                await Assert.That(shader2.VariableSourceMax).IsEqualTo(shader1.VariableSourceMax);

                // Binary stores one hash, KV3 stores all hashes
                // Assert.That(shader1.HashesMD5, Is.EqualTo(shader2.HashesMD5));
            }

            using (Assert.Multiple())
            {
                await Assert.That(shader2.DynamicComboArray).Count().IsEqualTo(shader1.DynamicComboArray.Length);
                for (var i = 0; i < shader1.DynamicComboArray.Length; i++)
                {
                    var combo1 = shader1.DynamicComboArray[i];
                    var combo2 = shader2.DynamicComboArray[i];

                    await Assert.That(combo2.Name).IsEqualTo(combo1.Name);
                    await Assert.That(combo2.CalculatedComboId).IsEqualTo(combo1.CalculatedComboId);
                    await Assert.That(combo2.AliasName).IsEqualTo(combo1.AliasName);
                    await Assert.That(combo2.ComboType).IsEqualTo(combo1.ComboType);
                    await Assert.That(combo2.ComboSourceType).IsEqualTo(combo1.ComboSourceType);
                    await Assert.That(combo2.FeatureComparisonValue).IsEqualTo(combo1.FeatureComparisonValue);
                    await Assert.That(combo2.RangeMin).IsEqualTo(combo1.RangeMin);
                    await Assert.That(combo2.RangeMax).IsEqualTo(combo1.RangeMax);
                    await Assert.That(combo2.Strings).IsEquivalentTo(combo1.Strings);
                }
            }

            using (Assert.Multiple())
            {
                await Assert.That(shader2.DynamicComboRules).Count().IsEqualTo(shader1.DynamicComboRules.Length);
                for (var i = 0; i < shader1.DynamicComboRules.Length; i++)
                {
                    var rule1 = shader1.DynamicComboRules[i];
                    var rule2 = shader2.DynamicComboRules[i];

                    await Assert.That(rule2.Rule).IsEqualTo(rule1.Rule);
                    await Assert.That(rule2.RuleType).IsEqualTo(rule1.RuleType);
                    await Assert.That(rule2.ConditionalTypes).IsEquivalentTo(rule1.ConditionalTypes);
                    await Assert.That(rule2.Indices).IsEquivalentTo(rule1.Indices);
                    await Assert.That(rule2.Values).IsEquivalentTo(rule1.Values);
                    await Assert.That(rule2.ExtraRuleData).IsEquivalentTo(rule1.ExtraRuleData);
                    await Assert.That(rule2.Description).IsEqualTo(rule1.Description);
                }
                await Assert.That(shader2.VariableDescriptions).Count().IsEqualTo(shader1.VariableDescriptions.Length);
                for (var i = 0; i < shader1.VariableDescriptions.Length; i++)
                {
                    var var1 = shader1.VariableDescriptions[i];
                    var var2 = shader2.VariableDescriptions[i];

                    await Assert.That(var2.Name).IsEqualTo(var1.Name);
                    await Assert.That(var2.UiGroup).IsEqualTo(var1.UiGroup);
                    await Assert.That(var2.StringData).IsEqualTo(var1.StringData);
                    await Assert.That(var2.UiType).IsEqualTo(var1.UiType);
                    await Assert.That(var2.UiStep).IsEqualTo(var1.UiStep);
                    await Assert.That(var2.VariableSource).IsEqualTo(var1.VariableSource);
                    await Assert.That(var2.DynExp).IsEquivalentTo(var1.DynExp);
                    await Assert.That(var2.UiVisibilityExp).IsEquivalentTo(var1.UiVisibilityExp);
                    await Assert.That(var2.SourceIndex).IsEqualTo(var1.SourceIndex);
                    await Assert.That(var2.VfxType).IsEqualTo(var1.VfxType);
                    await Assert.That(var2.RegisterType).IsEqualTo(var1.RegisterType);
                    await Assert.That(var2.ContextStateAffectedByVariable).IsEqualTo(var1.ContextStateAffectedByVariable);
                    await Assert.That(var2.RegisterElements).IsEqualTo(var1.RegisterElements);
                    await Assert.That(var2.ExtConstantBufferId).IsEqualTo(var1.ExtConstantBufferId);
                    await Assert.That(var2.DefaultInputTexture).IsEqualTo(var1.DefaultInputTexture);
                    await Assert.That(var2.IntDefs).IsEquivalentTo(var1.IntDefs).Because(var2.Name);
                    await Assert.That(var2.IntMins).IsEquivalentTo(var1.IntMins).Because(var2.Name);
                    await Assert.That(var2.IntMaxs).IsEquivalentTo(var1.IntMaxs).Because(var2.Name);
                    await Assert.That(var2.FloatDefs).IsEquivalentTo(var1.FloatDefs).Because(var2.Name);
                    await Assert.That(var2.FloatMins).IsEquivalentTo(var1.FloatMins).Because(var2.Name);
                    await Assert.That(var2.FloatMaxs).IsEquivalentTo(var1.FloatMaxs).Because(var2.Name);
                    await Assert.That(var2.ImageFormat).IsEqualTo(var1.ImageFormat);
                    await Assert.That(var2.ChannelCount).IsEqualTo(var1.ChannelCount);
                    await Assert.That(var2.ChannelIndices).IsEquivalentTo(var1.ChannelIndices);
                    await Assert.That(var2.ColorMode).IsEqualTo(var1.ColorMode);
                    await Assert.That(var2.ImageSuffix).IsEqualTo(var1.ImageSuffix);
                    await Assert.That(var2.ImageProcessor).IsEqualTo(var1.ImageProcessor);

                    await Assert.That(var2.MinPrecisionBits).IsEqualTo(var1.MinPrecisionBits);
                    await Assert.That(var2.LayerId).IsEqualTo(var1.LayerId);
                    await Assert.That(var2.AllowLayerOverride).IsEqualTo(var1.AllowLayerOverride);
                    await Assert.That(var2.MaxRes).IsEqualTo(var1.MaxRes);
                    await Assert.That(var2.IsLayerConstant).IsEqualTo(var1.IsLayerConstant);
                }
            }

            using (Assert.Multiple())
            {
                await Assert.That(shader2.StaticComboEntries).Count().IsEqualTo(shader1.StaticComboEntries.Count);

                var combo1 = shader1.GetStaticCombo(0);
                var combo2 = shader2.GetStaticCombo(0);

                // KV3 has one less item in some arrays
                const int OneLessItemKV3 = 1;

                await Assert.That(combo2.StaticComboId).IsEqualTo(combo1.StaticComboId);
                await Assert.That(combo2.VShaderInputs).IsEquivalentTo(combo1.VShaderInputs, CollectionOrdering.Matching);
                await Assert.That(combo2.ConstantBufferBindInfoFlags).IsEquivalentTo(combo1.ConstantBufferBindInfoFlags[..^OneLessItemKV3], CollectionOrdering.Matching);
                await Assert.That(combo2.ConstantBufferBindInfoSlots).IsEquivalentTo(combo1.ConstantBufferBindInfoSlots[..^OneLessItemKV3], CollectionOrdering.Matching);
                await Assert.That(combo2.ConstantBufferSize).IsEqualTo(combo1.ConstantBufferSize);
                await Assert.That(combo2.Flagbyte0).IsEqualTo(combo1.Flagbyte0);
                await Assert.That(combo2.Flagbyte1).IsEqualTo(combo1.Flagbyte1);
                await Assert.That(combo2.Flagbyte2).IsEqualTo(combo1.Flagbyte2);

                static async Task TestVfxVariableIndexArray(VfxVariableIndexArray binary, VfxVariableIndexArray kv3)
                {
                    using var _ = Assert.Multiple();
                    await Assert.That(kv3.BlockId).IsEqualTo(binary.BlockId);
                    await Assert.That(kv3.FirstRenderStateElement).IsEqualTo(binary.FirstRenderStateElement);
                    await Assert.That(kv3.FirstConstantElement).IsEqualTo(binary.FirstConstantElement);
                    await Assert.That(kv3.Fields).IsEquivalentTo(binary.Fields, CollectionOrdering.Matching);
                }

                await TestVfxVariableIndexArray(combo1.VariablesFromStaticCombo, combo2.VariablesFromStaticCombo);
                // one less
                await Assert.That(combo2.DynamicComboVariables).Count().IsEqualTo(combo1.DynamicComboVariables.Length - OneLessItemKV3);
                for (var i = 0; i < combo2.DynamicComboVariables.Length; i++)
                {
                    await TestVfxVariableIndexArray(combo1.DynamicComboVariables[i], combo2.DynamicComboVariables[i]);
                }
                await Assert.That(combo2.DynamicCombos).Count().IsEqualTo(combo1.DynamicCombos.Length);
                for (var i = 0; i < combo1.DynamicCombos.Length; i++)
                {
                    var dyn1 = combo1.DynamicCombos[i];
                    var dyn2 = combo2.DynamicCombos[i];

                    await Assert.That(dyn2.ShaderFileId).IsEqualTo(dyn1.ShaderFileId);
                    await Assert.That(dyn2.DynamicComboId).IsEqualTo(dyn1.DynamicComboId);

                    // Source pointer is binary only
                    // Assert.That(dyn2.SourcePointer, Is.EqualTo(dyn1.SourcePointer));

                    var psRenderState1 = dyn1 as VfxRenderStateInfoPixelShader;
                    var psRenderState2 = dyn2 as VfxRenderStateInfoPixelShader;

                    var depth1 = psRenderState1!.DepthStencilStateDesc!.Value;
                    var depth2 = psRenderState2!.DepthStencilStateDesc!.Value;


                    await Assert.That(depth2.DepthWriteEnable).IsEqualTo(depth1.DepthWriteEnable);
                    await Assert.That(depth2.DepthFunc).IsEqualTo(depth1.DepthFunc);
                    await Assert.That(depth2.DepthTestEnable).IsEqualTo(depth1.DepthTestEnable);
                    await Assert.That(depth2.StencilEnable).IsEqualTo(depth1.StencilEnable);
                    await Assert.That(depth2.StencilReadMask).IsEqualTo(depth1.StencilReadMask);
                    await Assert.That(depth2.StencilWriteMask).IsEqualTo(depth1.StencilWriteMask);
                    await Assert.That(depth2.FrontStencilFunc).IsEqualTo(depth1.FrontStencilFunc);
                    await Assert.That(depth2.FrontStencilPassOp).IsEqualTo(depth1.FrontStencilPassOp);
                    await Assert.That(depth2.FrontStencilFailOp).IsEqualTo(depth1.FrontStencilFailOp);
                    await Assert.That(depth2.FrontStencilDepthFailOp).IsEqualTo(depth1.FrontStencilDepthFailOp);
                    await Assert.That(depth2.BackStencilFunc).IsEqualTo(depth1.BackStencilFunc);
                    await Assert.That(depth2.BackStencilPassOp).IsEqualTo(depth1.BackStencilPassOp);
                    await Assert.That(depth2.BackStencilFailOp).IsEqualTo(depth1.BackStencilFailOp);
                    await Assert.That(depth2.BackStencilDepthFailOp).IsEqualTo(depth1.BackStencilDepthFailOp);


                    var raster1 = psRenderState1.RasterizerStateDesc!.Value;
                    var raster2 = psRenderState2.RasterizerStateDesc!.Value;
                    await Assert.That(raster2.FillMode).IsEqualTo(raster1.FillMode);
                    await Assert.That(raster2.CullMode).IsEqualTo(raster1.CullMode);
                    await Assert.That(raster2.DepthClipEnable).IsEqualTo(raster1.DepthClipEnable);
                    await Assert.That(raster2.MultisampleEnable).IsEqualTo(raster1.MultisampleEnable);
                    await Assert.That(raster2.DepthBias).IsEqualTo(raster1.DepthBias);
                    await Assert.That(raster2.DepthBiasClamp).IsEqualTo(raster1.DepthBiasClamp);
                    await Assert.That(raster2.SlopeScaledDepthBias).IsEqualTo(raster1.SlopeScaledDepthBias);

                    var blend1 = psRenderState1.BlendStateDesc!.Value;
                    var blend2 = psRenderState2.BlendStateDesc!.Value;

                    await Assert.That(blend2.AlphaToCoverageEnable).IsEqualTo(blend1.AlphaToCoverageEnable);
                    await Assert.That(blend2.IndependentBlendEnable).IsEqualTo(blend1.IndependentBlendEnable);

                    for (var t = 0; t < RsBlendStateDesc.MaxRenderTargets; t++)
                    {
                        await Assert.That(blend2.BlendEnable[t]).IsEqualTo(blend1.BlendEnable[t]);
                        await Assert.That(blend2.SrcBlend[t]).IsEqualTo(blend1.SrcBlend[t]);
                        await Assert.That(blend2.DestBlend[t]).IsEqualTo(blend1.DestBlend[t]);
                        await Assert.That(blend2.BlendOp[t]).IsEqualTo(blend1.BlendOp[t]);
                        await Assert.That(blend2.SrcBlendAlpha[t]).IsEqualTo(blend1.SrcBlendAlpha[t]);
                        await Assert.That(blend2.DestBlendAlpha[t]).IsEqualTo(blend1.DestBlendAlpha[t]);
                        await Assert.That(blend2.BlendOpAlpha[t]).IsEqualTo(blend1.BlendOpAlpha[t]);
                        await Assert.That(blend2.RenderTargetWriteMask[t]).IsEqualTo(blend1.RenderTargetWriteMask[t]);
                        await Assert.That(blend2.SrgbWriteEnable[t]).IsEqualTo(blend1.SrgbWriteEnable[t]);
                    }
                }

                await Assert.That(combo2.Attributes).IsEquivalentTo(combo1.Attributes, CollectionOrdering.Matching);
                await Assert.That(combo2.ShaderFiles).Count().IsEqualTo(combo1.ShaderFiles.Length);
            }
        }

        [Test]
        public async Task TestVcsFileName()
        {
            var testCases = new (string FileName, string ShaderName, VcsPlatformType Platform, VcsShaderModelType ShaderModel, VcsProgramType ProgramType)[]
            {
                ("/sourcedir/multiblend_pcgl_40_ps.vcs", "multiblend", VcsPlatformType.PCGL, VcsShaderModelType._40, VcsProgramType.PixelShader),
                ("/sourcedir/solid_sky_pcgl_30_features.vcs", "solid_sky", VcsPlatformType.PCGL, VcsShaderModelType._30, VcsProgramType.Features),
                ("/sourcedir/copytexture_pc_30_ps.vcs", "copytexture", VcsPlatformType.PC, VcsShaderModelType._30, VcsProgramType.PixelShader),
                ("/sourcedir/copytexture_pc_40_ps.vcs", "copytexture", VcsPlatformType.PC, VcsShaderModelType._40, VcsProgramType.PixelShader),
                ("/sourcedir/deferred_shading_pc_41_ps.vcs", "deferred_shading", VcsPlatformType.PC, VcsShaderModelType._41, VcsProgramType.PixelShader),
                ("/sourcedir/bloom_dota_mobile_gles_30_ps.vcs", "bloom_dota", VcsPlatformType.MOBILE_GLES, VcsShaderModelType._30, VcsProgramType.PixelShader),
                ("/sourcedir/cs_volumetric_fog_vulkan_50_cs.vcs", "cs_volumetric_fog", VcsPlatformType.VULKAN, VcsShaderModelType._50, VcsProgramType.ComputeShader),
                ("/sourcedir/bloom_dota_ios_vulkan_40_ps.vcs", "bloom_dota", VcsPlatformType.IOS_VULKAN, VcsShaderModelType._40, VcsProgramType.PixelShader),
                ("/sourcedir/flow_map_preview_android_vulkan_40_vs.vcs", "flow_map_preview", VcsPlatformType.ANDROID_VULKAN, VcsShaderModelType._40, VcsProgramType.VertexShader),
            };

            foreach (var testCase in testCases)
            {
                var result = ComputeVCSFileName(testCase.FileName);
                var opposite = ComputeVCSFileName(testCase.ShaderName, testCase.ProgramType, testCase.Platform, testCase.ShaderModel);

                using (Assert.Multiple())
                {
                    await Assert.That(result.ShaderName).IsEqualTo(testCase.ShaderName);
                    await Assert.That(result.PlatformType).IsEqualTo(testCase.Platform);
                    await Assert.That(result.ShaderModelType).IsEqualTo(testCase.ShaderModel);
                    await Assert.That(result.ProgramType).IsEqualTo(testCase.ProgramType);
                    await Assert.That(opposite).IsEqualTo(Path.GetFileName(testCase.FileName));
                }
            }
        }

        [Test]
        public async Task CompiledShaderInResourceThrows()
        {
            var path = Path.Combine(ShadersDir, "vcs64_error_pcgl_40_ps.vcs");
            using var resource = new Resource();

            var ex = Assert.ThrowsExactly<InvalidDataException>(() => resource.Read(path));
            await Assert.That(ex).IsNotNull();
        }

        [Test]
        public async Task TestZFrameWriteSequences()
        {
            var path = Path.Combine(ShadersDir, "vcs64_error_pcgl_40_ps.vcs");
            using var shader = new VfxProgramData();
            shader.Read(path);

            var zFrameFile = shader.GetStaticCombo(0);
            using var sw = new IndentedTextWriter();
            var zframeSummary = new PrintZFrameSummary(zFrameFile, sw);

            var wsCount = zframeSummary.GetUniqueWriteSequences().Count;
            await Assert.That(wsCount).IsEqualTo(1);

            var zBlockToWS = zframeSummary.GetBlockToUniqueSequenceMap();
            var expected = new Dictionary<int, int>
            {
                {-1, 0},
                {0, 0},
            };

            // Only the block to sequence mapping matters, not the order the entries come out in.
            await Assert.That(zBlockToWS).IsEquivalentTo(expected);
        }

        [Test]
        public async Task TestChannelMapping()
        {
            using (Assert.Multiple())
            {
                await Assert.That(ChannelMapping.R.PackedValue).IsEqualTo((uint)0xFFFFFF00);
                await Assert.That(ChannelMapping.G.PackedValue).IsEqualTo((uint)0xFFFFFF01);
                await Assert.That(ChannelMapping.B.PackedValue).IsEqualTo((uint)0xFFFFFF02);
                await Assert.That(ChannelMapping.A.PackedValue).IsEqualTo((uint)0xFFFFFF03);
                await Assert.That(ChannelMapping.RGB.PackedValue).IsEqualTo((uint)0xFF020100);
                await Assert.That(ChannelMapping.RGBA.PackedValue).IsEqualTo((uint)0x03020100);

                await Assert.That(ChannelMapping.RGBA.Channels[0]).IsEqualTo(ChannelMapping.Channel.R);
                await Assert.That(ChannelMapping.RGBA.Channels[1]).IsEqualTo(ChannelMapping.Channel.G);
                await Assert.That(ChannelMapping.AG.Channels[1]).IsEqualTo(ChannelMapping.Channel.G);

                await Assert.That(ChannelMapping.RGBA.Count).IsEqualTo(4);
                await Assert.That(ChannelMapping.RGB.Count).IsEqualTo(3);
                await Assert.That(ChannelMapping.RG.Count).IsEqualTo(2);
                await Assert.That(ChannelMapping.G.Count).IsEqualTo(1);
                await Assert.That(ChannelMapping.NULL.Count).IsZero();
                await Assert.That(ChannelMapping.RGBA.ValidChannels).IsEquivalentTo(new[] { ChannelMapping.Channel.R, ChannelMapping.Channel.G, ChannelMapping.Channel.B, ChannelMapping.Channel.A }, CollectionOrdering.Matching);
                await Assert.That(ChannelMapping.RGB.ValidChannels).IsEquivalentTo(new[] { ChannelMapping.Channel.R, ChannelMapping.Channel.G, ChannelMapping.Channel.B }, CollectionOrdering.Matching);
                await Assert.That(ChannelMapping.RG.ValidChannels).IsEquivalentTo(new[] { ChannelMapping.Channel.R, ChannelMapping.Channel.G }, CollectionOrdering.Matching);
                await Assert.That(ChannelMapping.AG.ValidChannels).IsEquivalentTo(new[] { ChannelMapping.Channel.A, ChannelMapping.Channel.G }, CollectionOrdering.Matching);
                await Assert.That(ChannelMapping.A.ValidChannels).IsEquivalentTo(new[] { ChannelMapping.Channel.A }, CollectionOrdering.Matching);
                await Assert.That(ChannelMapping.R.ValidChannels).IsEquivalentTo(new[] { ChannelMapping.Channel.R }, CollectionOrdering.Matching);

                await Assert.That((byte)ChannelMapping.R).IsZero();
                await Assert.That((byte)ChannelMapping.G).IsEqualTo((byte)0x01);
                await Assert.That((byte)ChannelMapping.B).IsEqualTo((byte)0x02);
                await Assert.That((byte)ChannelMapping.A).IsEqualTo((byte)0x03);

                await Assert.That(ChannelMapping.R).IsEqualTo(ChannelMapping.FromUInt32(0xFFFFFF00));
                await Assert.That(ChannelMapping.G).IsEqualTo(ChannelMapping.FromUInt32(0xFFFFFF01));
                await Assert.That(ChannelMapping.AG).IsEqualTo(ChannelMapping.FromUInt32(0xFFFF0103));

                // Version 67 and newer pack the destination channel into the low nibble of each byte
                await Assert.That(ChannelMapping.RGBA).IsEqualTo(ChannelMapping.FromUInt32(0x33221100, packedDestinations: true));
                await Assert.That(ChannelMapping.AG).IsEqualTo(ChannelMapping.FromUInt32(0xFFFF1130, packedDestinations: true));

                await Assert.That(ChannelMapping.RGBA.Destinations).IsEquivalentTo(new byte[] { 0, 1, 2, 3 }, CollectionOrdering.Matching);
                await Assert.That(ChannelMapping.AG.Destinations).IsEquivalentTo(new byte[] { 0, 1 }, CollectionOrdering.Matching);

                var rotated = ChannelMapping.FromUInt32(0x23120130, packedDestinations: true);
                await Assert.That(rotated.ValidChannels).IsEquivalentTo(new[] { ChannelMapping.Channel.A, ChannelMapping.Channel.R, ChannelMapping.Channel.G, ChannelMapping.Channel.B }, CollectionOrdering.Matching);
                await Assert.That(rotated.Destinations).IsEquivalentTo(new byte[] { 0, 1, 2, 3 }, CollectionOrdering.Matching);

                var offset = ChannelMapping.FromUInt32(0xFFFF1201, packedDestinations: true);
                await Assert.That(offset.ValidChannels).IsEquivalentTo(new[] { ChannelMapping.Channel.R, ChannelMapping.Channel.G }, CollectionOrdering.Matching);
                await Assert.That(offset.Destinations).IsEquivalentTo(new byte[] { 1, 2 }, CollectionOrdering.Matching);

                await Assert.That(ChannelMapping.R.ToString()).IsEqualTo("R");
                await Assert.That(ChannelMapping.G.ToString()).IsEqualTo("G");
                await Assert.That(ChannelMapping.B.ToString()).IsEqualTo("B");
                await Assert.That(ChannelMapping.A.ToString()).IsEqualTo("A");
                await Assert.That(ChannelMapping.AG.ToString()).IsEqualTo("AG");
                await Assert.That(ChannelMapping.RGB.ToString()).IsEqualTo("RGB");
                await Assert.That(ChannelMapping.NULL.ToString()).IsEqualTo("0xFFFFFFFF");

                Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ChannelMapping.FromChannels(0x04));
                Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ChannelMapping.FromChannels(0x05));

                await Assert.That(ChannelMapping.FromChannels(0xFF)).IsEqualTo(ChannelMapping.NULL);
            }
        }

        [Test]
        public async Task VfxShaderExtract_ReplacesWholeIdentifiersOnly()
        {
            using (Assert.Multiple())
            {
                await Assert.That(ShaderExtract.ReplaceIdentifier("g_flCubeMapBlur*g_flCubeMapBlurAmount", "g_flCubeMapBlur", "this")).IsEqualTo("this*g_flCubeMapBlurAmount");
                await Assert.That(ShaderExtract.ReplaceIdentifier("float2(g_vScale.x,g_vScale.y)", "g_vScale", "this")).IsEqualTo("float2(this.x,this.y)");
                await Assert.That(ShaderExtract.ReplaceIdentifier("g_flAmount", "g_flAmountExtra", "this")).IsEqualTo("g_flAmount");
            }
        }

        [Test]
        public async Task VfxShaderExtract_RenderStateEnumNames()
        {
            var colorWriteEnable = typeof(RsColorWriteEnableBits);
            var cullMode = typeof(RsCullMode);

            using (Assert.Multiple())
            {
                await Assert.That(ShaderUtilHelpers.GetEnumName(colorWriteEnable, 3)).IsEqualTo("R|G");
                await Assert.That(ShaderUtilHelpers.GetEnumName(colorWriteEnable, 5)).IsEqualTo("R|B");
                await Assert.That(ShaderUtilHelpers.GetEnumName(colorWriteEnable, 7)).IsEqualTo("R|G|B");
                await Assert.That(ShaderUtilHelpers.GetEnumName(colorWriteEnable, 14)).IsEqualTo("G|B|A");
                await Assert.That(ShaderUtilHelpers.GetEnumName(colorWriteEnable, 15)).IsEqualTo("All");
                await Assert.That(ShaderUtilHelpers.GetEnumName(colorWriteEnable, 0)).IsEqualTo("None");
                await Assert.That(ShaderUtilHelpers.GetEnumName(cullMode, 42)).IsEqualTo("42");
            }
        }

        [Test]
        public async Task VfxShaderExtract_Invalid()
        {
            var path = Path.Combine(ShadersDir, "vcs64_error_pcgl_40_ps.vcs");
            using var shader = new VfxProgramData();
            shader.Read(path);

            var ex = Assert.ThrowsExactly<InvalidOperationException>(() => _ = new ShaderExtract(ShaderCollection.FromEnumerable([shader])));

            Debug.Assert(ex != null);
            await Assert.That(ex).IsNotNull();
            await Assert.That(ex.Message).Contains("cannot continue without at least a features file");
        }

        [Test]
        public async Task VfxShaderExtract_Minimal()
        {
            var path = Path.Combine(ShadersDir, "vcs64_error_pc_40_features.vcs");
            using var shader = new VfxProgramData();
            shader.Read(path);

            var extract = new ShaderExtract(ShaderCollection.FromEnumerable([shader]));

            var vfx = extract.ToVFX(ShaderExtract.ShaderExtractParams.Inspect);
            vfx = extract.ToVFX(ShaderExtract.ShaderExtractParams.Export);

            await Assert.That(vfx.VfxContent).Contains("Description = \"Error shader\"");
            await Assert.That(vfx.VfxContent).Contains("DevShader = true");
        }

        [Test]
        public async Task VfxShaderExtract_OptionsTest()
        {
            using var collection = new ShaderCollection();
            foreach (var file in Directory.GetFiles(ShadersDir, "vcs64_error_pc_40_*.vcs"))
            {
                var shader = new VfxProgramData();

                try
                {
                    shader.Read(file);
                    collection.Add(shader);
                    shader = null;
                }
                finally
                {
                    shader?.Dispose();
                }
            }

            var extract = new ShaderExtract(collection);

            var optionsToTest = new[]
            {
                ShaderExtract.ShaderExtractParams.Inspect,
                ShaderExtract.ShaderExtractParams.Export,
                new ShaderExtract.ShaderExtractParams { },
                new ShaderExtract.ShaderExtractParams { CollapseBuffers_InInclude = true },
                new ShaderExtract.ShaderExtractParams { StaticComboReadingCap = -1 },
                new ShaderExtract.ShaderExtractParams { StaticComboReadingCap = 0 },
                new ShaderExtract.ShaderExtractParams { StaticComboReadingCap = 1 },
                new ShaderExtract.ShaderExtractParams { StaticComboAttributes_NoSeparateGlobals = true },
                new ShaderExtract.ShaderExtractParams { StaticComboAttributes_NoConditionalReduce = true },
            };

            foreach (var options in optionsToTest)
            {
                var vfx = extract.ToVFX(options);
                await Assert.That(vfx.VfxContent).Contains("Description = \"Error shader\"");
                await Assert.That(vfx.VfxContent).Contains("DevShader = true");
            }
        }

        public static IEnumerable<(string, int, int)> SpirvReflectionTestCases()
        {
            yield return ("vcs65_compute_depthbin_cullbits_vulkan_50_cs.vcs", 0, 0);
            yield return ("vcs68_tower_force_field_vulkan_40_vs.vcs", 0, 9);
            yield return ("vcs68_tower_force_field_vulkan_40_ps.vcs", 1, 1);
            yield return ("vcs68_csgo_simple_2way_blend_vulkan_60_rtx.vcs", 0x6, 0);
            yield return ("vcs68_test_vulkan_60_ms.vcs", 0, 1);
            yield return ("vcs69_downsample_depth_cs_vulkan_50_cs.vcs", 0, 0x20);
            yield return ("vcs69_zstd5_npr_dummy_vulkan_50_vs.vcs", 0, 0);
            yield return ("vcs69_bloom_vulkan_40_ps.vcs", 0, 0);
            yield return ("vcs70_resource_bloom_vulkan_40_ps.vcs", 0, 0);
        }

        [Test, MethodDataSource(nameof(SpirvReflectionTestCases))]
        public async Task TestSpirvReflection(string shaderFile, int staticCombo, int dynamicCombo)
        {
            if (!IsSpirvCrossAvailable())
            {
                Skip.Test("There are no native binaries for SPIR-V on arm linux yet.");
                return;
            }

            var path = Path.Combine(ShadersDir, shaderFile);
            using var shader = new VfxProgramData();
            shader.Read(path);

            var staticComboEntry = shader.GetStaticCombo(staticCombo);
            var dynamicComboEntry = staticComboEntry.DynamicCombos[dynamicCombo];
            var code = staticComboEntry.ShaderFiles[dynamicComboEntry.ShaderFileId].GetDecompiledFile();
            code = code.Replace(StringToken.VRF_GENERATOR, "VRF-TEST", StringComparison.Ordinal);

            var referencePath = Path.Combine(ShadersDir, "SpirvOutput", $"{shaderFile}.glsl");

            /*{
                var shadersDirRepo = Path.Combine(TestContext.TestDirectory!, "../../", "Files", "Shaders");
                var referencePathRepo = Path.Combine(shadersDirRepo, "SpirvOutput", $"{shaderFile}.glsl");
                File.WriteAllText(referencePathRepo, code);
                return;
            }*/

            var reference = await File.ReadAllTextAsync(referencePath);
            await Assert.That(code).IsEqualTo(reference).IgnoringWhitespace().Because("Spirv reflection output does not match reference.");
        }

        [Test]
        public async Task TestDepthStencilStateBitLayouts()
        {
            // Depth test+write with LessEqual, stencil disabled with Always funcs and full masks. The bit layout changed in version 71.
            var v71 = new RsDepthStencilStateDesc(0xFFFF00000077000FUL, 71);

            using (Assert.Multiple())
            {
                await Assert.That(v71.DepthTestEnable).IsTrue();
                await Assert.That(v71.DepthWriteEnable).IsTrue();
                await Assert.That(v71.DepthFunc).IsEqualTo(RsComparison.LessEqual);
                await Assert.That(v71.StencilEnable).IsFalse();
                await Assert.That(v71.FrontStencilFunc).IsEqualTo(RsComparison.Always);
                await Assert.That(v71.BackStencilFunc).IsEqualTo(RsComparison.Always);
                await Assert.That(v71.StencilReadMask).IsEqualTo((byte)0xFF);
                await Assert.That(v71.StencilWriteMask).IsEqualTo((byte)0xFF);
            }

            // Value from vcs70 and older: depth disabled, LessEqual, Always funcs.
            var v70 = new RsDepthStencilStateDesc(0xFFFF01C01C000300UL, 70);

            using (Assert.Multiple())
            {
                await Assert.That(v70.DepthTestEnable).IsFalse();
                await Assert.That(v70.DepthWriteEnable).IsFalse();
                await Assert.That(v70.DepthFunc).IsEqualTo(RsComparison.LessEqual);
                await Assert.That(v70.StencilEnable).IsFalse();
                await Assert.That(v70.FrontStencilFunc).IsEqualTo(RsComparison.Always);
                await Assert.That(v70.BackStencilFunc).IsEqualTo(RsComparison.Always);
                await Assert.That(v70.StencilReadMask).IsEqualTo((byte)0xFF);
                await Assert.That(v70.StencilWriteMask).IsEqualTo((byte)0xFF);
            }
        }

        [Test]
        public async Task TestUiGroup()
        {
            var testCases = new Dictionary<string, UiGroup>
            {
                ["heading,10/2"] = new("heading", 10, variableOrder: 2),
                ["heading,12/group,12/5"] = new("heading", 12, "group", 12, 5),
                ["Interaction Effects, 500,20"] = new("Interaction Effects", headingOrder: 500),

                [string.Empty] = new(),
                ["h,1/g,2"] = new("h", 1, variableOrder: 2),
                ["h,1/g"] = new("h", 1),
                ["h,1"] = new("h", 1),
                ["h"] = new("h"),

                ["//////"] = new(),
                ["z,z,z/z,z,z,z/z,z,z,z/,z,z,z"] = new(heading: "z,z,z", group: "z,z,z,z"),
            };

            foreach (var (compactString, expected) in testCases)
            {
                var parsed = UiGroup.FromCompactString(compactString);
                using (Assert.Multiple())
                {
                    await Assert.That(parsed.Heading).IsEqualTo(expected.Heading);
                    await Assert.That(parsed.HeadingOrder).IsEqualTo(expected.HeadingOrder);
                    await Assert.That(parsed.Group).IsEqualTo(expected.Group);
                    await Assert.That(parsed.GroupOrder).IsEqualTo(expected.GroupOrder);
                    await Assert.That(parsed.VariableOrder).IsEqualTo(expected.VariableOrder);
                }
            }
        }
    }
}
