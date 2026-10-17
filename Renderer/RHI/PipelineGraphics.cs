using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

using ValveResourceFormat.Renderer.RHI.ShaderCompile;

namespace ValveResourceFormat.Renderer.RHI
{
    public struct AttributeDescription
    {
        public string SemanticName;
        public uint SemanticIndex;
        public uint Offset;
        public VkFormat Format;
    }
    public struct BindingDescription
    {
        public uint binding;
        public uint stride;
        public AttributeDescription[] attributes;
    }

    public struct BlendStateDescription
    {
        public bool BlendEnable = false;
        public VkBlendFactor srcBlendFactor = VkBlendFactor.SrcAlpha;
        public VkBlendFactor dstBlendFactor = VkBlendFactor.OneMinusSrcAlpha;
        public BlendStateDescription() { }
    }

    public class PipelineGraphics : Pipeline
    {
#if DEBUG
        public BindingDescription[]? BindingDescriptions;
        public VkFormat ColorTargetFormat;
        public VkFormat DepthTargetFormat;
        public BlendStateDescription BlendStateDescription;
#endif

        /// <summary>
        /// Creates a graphics pipeline with optional alpha blending support.
        /// </summary>
        /// <param name="shader">The specialised shader to compile.</param>
        /// <param name="colorTargetFormat">Color attachment format (0 = no color target).</param>
        /// <param name="depthTargetFormat">Depth attachment format (0 = no depth target).</param>
        /// <param name="bindingDescriptions">Vertex binding descriptions (nullable for compute/mesh pipelines).</param>
        /// <param name="blendState">Blending state for, among others, alpha blending.</param>
        /// <param name="depthWriteEnable">Whether depth writes are enabled (default true).</param>
        unsafe public PipelineGraphics(SpecialisedShader shader, VkFormat colorTargetFormat = 0, VkFormat depthTargetFormat = 0, BindingDescription[]? bindingDescriptions = null, BlendStateDescription blendState = default(BlendStateDescription), bool depthWriteEnable = true)
        {
#if DEBUG
            ColorTargetFormat = colorTargetFormat;
            DepthTargetFormat = depthTargetFormat;
            BindingDescriptions = bindingDescriptions;
            BlendStateDescription = blendState;
#endif

            ParameterLayout = shader.MaterialParameters;

            VkShaderModule shaderModule;
            fixed (byte* pSpirv = shader.Spirv.Span)
            {
                VkShaderModuleCreateInfo shaderModuleInfo = new VkShaderModuleCreateInfo
                {
                    codeSize = (nuint)shader.Spirv.Length,
                    pCode = (uint*)pSpirv
                };

                RenderDevice!.VkDeviceApi.vkCreateShaderModule(shaderModuleInfo, &shaderModule);
            }

            byte[] vertName = Encoding.ASCII.GetBytes("vertMain\0");
            byte[] fragName = Encoding.ASCII.GetBytes("fragMain\0");

            VkPipelineShaderStageCreateInfo[] shaderModules = new VkPipelineShaderStageCreateInfo[2];


            //low safety. We could really check and decide on what we prefer. Potentially even based on global state, like mesh shading support.
            var bitmask = VkShaderStageFlags.Vertex | VkShaderStageFlags.MeshEXT | VkShaderStageFlags.Fragment;

            var matched = shader.Stages
                .Where(s => (s.Key & bitmask) != 0)
                .ToArray();

            using var names = new VkStringArray(matched.Select(s => s.Value).ToArray());

            var index = 0;
            foreach (var stage in matched)
            {
                shaderModules[index].sType = VkStructureType.PipelineShaderStageCreateInfo;
                shaderModules[index].stage = stage.Key;
                shaderModules[index].module = shaderModule;
                shaderModules[index].pName = *((byte**)names + index);

                index++;
            }


            // Determine how many color attachments to report (0 or 1)
            uint colorAttachmentCount = colorTargetFormat != 0 ? 1u : 0;

            VkPipelineRenderingCreateInfo renderingCreateInfo = new VkPipelineRenderingCreateInfo
            {
                colorAttachmentCount = colorAttachmentCount,
                pColorAttachmentFormats = colorTargetFormat != 0 ? &colorTargetFormat : null,
                depthAttachmentFormat = depthTargetFormat
            };

            // Build vertex input state from binding descriptions, or leave it empty for compute/mesh pipelines.
            VkVertexInputAttributeDescription[] vertexInputAttributes;
            VkVertexInputBindingDescription[] vertexBindingDescriptions;

            if (bindingDescriptions != null && bindingDescriptions.Length > 0)
            {
                // Build a lookup from (semanticName, semanticIndex) -> binding description index.
                var attributeToBinding = new Dictionary<(string SemanticName, uint SemanticIndex), int>();
                for (int i = 0; i < bindingDescriptions.Length; i++)
                {
                    foreach (var attr in bindingDescriptions[i].attributes)
                    {
                        attributeToBinding[(attr.SemanticName, attr.SemanticIndex)] = i;
                    }
                }

                // Build VkVertexInputAttributeDescription for each shader vertex input.
                // Only include attributes that have a matching entry in the binding descriptions.
                var matchedAttributes = new List<VkVertexInputAttributeDescription>();
                foreach (var vi in shader.VertexInputs)
                {
                    if (!attributeToBinding.TryGetValue((vi.SemanticName, vi.SemanticIndex), out var bindingIdx))
                    {
                        // Shader expects this attribute but it's not provided. Skip it.
                        continue;
                    }

                    var bd = bindingDescriptions[bindingIdx];
                    var attrDesc = bd.attributes.First(a => a.SemanticName == vi.SemanticName && a.SemanticIndex == vi.SemanticIndex);

                    matchedAttributes.Add(new VkVertexInputAttributeDescription
                    {
                        location = vi.Location,
                        binding = bd.binding,
                        format = attrDesc.Format,
                        offset = attrDesc.Offset,
                    });
                }

                vertexInputAttributes = [.. matchedAttributes];

                // Collect unique bindings and build VkVertexInputBindingDescription for each.
                var uniqueBindings = new Dictionary<uint, VkVertexInputBindingDescription>();
                foreach (var bd in bindingDescriptions)
                {
                    uniqueBindings[bd.binding] = new VkVertexInputBindingDescription
                    {
                        binding = bd.binding,
                        stride = bd.stride,
                        inputRate = VkVertexInputRate.Vertex,
                    };
                }

                vertexBindingDescriptions = [.. uniqueBindings.Values];
            }
            else
            {
                vertexInputAttributes = [];
                vertexBindingDescriptions = [];
            }

            fixed (VkVertexInputAttributeDescription* pVertexAttributes = vertexInputAttributes)
            fixed (VkVertexInputBindingDescription* pVertexBindings = vertexBindingDescriptions)
            fixed (VkPipelineShaderStageCreateInfo* pShaderStageCreateInfos = shaderModules)
            {
                VkPipelineVertexInputStateCreateInfo vertexInputStateCreateInfo = new()
                {
                    vertexAttributeDescriptionCount = (uint)vertexInputAttributes.Length,
                    pVertexAttributeDescriptions = pVertexAttributes,
                    vertexBindingDescriptionCount = (uint)vertexBindingDescriptions.Length,
                    pVertexBindingDescriptions = pVertexBindings,
                }; 

                // --- Input assembly: how vertices are grouped into primitives ---
                VkPipelineInputAssemblyStateCreateInfo inputAssemblyStateCreateInfo = new()
                {
                    topology = VkPrimitiveTopology.TriangleList,
                    primitiveRestartEnable = false
                };

                // --- Viewport/scissor: counts only, actual values set dynamically at draw time ---
                VkPipelineViewportStateCreateInfo viewportStateCreateInfo = new()
                {
                    viewportCount = 1,
                    pViewports = null,
                    scissorCount = 1,
                    pScissors = null
                };

                VkDynamicState* dynamicStates = stackalloc VkDynamicState[]
                {
                    VkDynamicState.Viewport,
                    VkDynamicState.Scissor,
                };

                VkPipelineDynamicStateCreateInfo dynamicStateCreateInfo = new()
                {
                    dynamicStateCount = 2,
                    pDynamicStates = dynamicStates
                };

                VkPipelineMultisampleStateCreateInfo msStateCreateinfo = new()
                {
                    rasterizationSamples = VkSampleCountFlags.Count1,
                    sampleShadingEnable = false,
                };

                // --- Depth/stencil state: enable depth testing if a depth format is provided ---
                VkPipelineDepthStencilStateCreateInfo dsStateDefault = new();
                VkPipelineDepthStencilStateCreateInfo* pDepthStencilState = null;
                if (depthTargetFormat != 0)
                {
                    VkPipelineDepthStencilStateCreateInfo dsState = new()
                    {
                        depthTestEnable = true,
                        depthWriteEnable = depthWriteEnable,
                        depthCompareOp = VkCompareOp.GreaterOrEqual, // Reverse Z
                        depthBoundsTestEnable = false,
                        stencilTestEnable = false,
                    };
                    pDepthStencilState = &dsState;
                }

                VkPipelineRasterizationStateCreateInfo pipelineRasterizationStateCreateInfo = new()
                {
                    polygonMode = VkPolygonMode.Fill,
                    lineWidth = 1.0f,
                    cullMode = VkCullModeFlags.Back,
                    frontFace = VkFrontFace.CounterClockwise,
                    depthClampEnable = false,
                    rasterizerDiscardEnable = false,
                    depthBiasEnable = false
                };

                // --- Color blend: one attachment, blending optionally enabled ---
                VkPipelineColorBlendAttachmentState colorBlendAttachmentState = new()
                {
                    blendEnable = blendState.BlendEnable,
                    srcColorBlendFactor = blendState.srcBlendFactor,
                    dstColorBlendFactor = blendState.dstBlendFactor,
                    colorBlendOp = VkBlendOp.Add,
                    srcAlphaBlendFactor = VkBlendFactor.One,
                    dstAlphaBlendFactor = blendState.dstBlendFactor,
                    alphaBlendOp = VkBlendOp.Add,
                    colorWriteMask = VkColorComponentFlags.R | VkColorComponentFlags.G |
                                      VkColorComponentFlags.B | VkColorComponentFlags.A
                };

                VkPipelineColorBlendStateCreateInfo colorBlendStateCreateInfo = new()
                {
                    logicOpEnable = false,
                    attachmentCount = 1,
                    pAttachments = &colorBlendAttachmentState
                };

                VkGraphicsPipelineCreateInfo pipelineInfo = new VkGraphicsPipelineCreateInfo
                {
                    pNext = &renderingCreateInfo,
                    stageCount = 2,
                    pStages = pShaderStageCreateInfos,
                    layout = RenderDevice.SharedPipelineLayout,
                    pVertexInputState = &vertexInputStateCreateInfo,
                    pInputAssemblyState = &inputAssemblyStateCreateInfo,
                    pViewportState = &viewportStateCreateInfo,
                    pRasterizationState = &pipelineRasterizationStateCreateInfo,
                    pMultisampleState = &msStateCreateinfo,
                    pDepthStencilState = pDepthStencilState,
                    pColorBlendState = &colorBlendStateCreateInfo,
                    pDynamicState = &dynamicStateCreateInfo,
                };

                var result = RenderDevice!.VkDeviceApi.vkCreateGraphicsPipeline(pipelineInfo, out var pipeline);

                Handle = pipeline;
            }

            RenderDevice!.VkDeviceApi.vkDestroyShaderModule(shaderModule);
        }


    }
}
