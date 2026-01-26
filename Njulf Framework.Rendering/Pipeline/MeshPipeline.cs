// SPDX-License-Identifier: MPL-2.0

using Silk.NET.Vulkan;
using Njulf_Framework.Rendering.Shaders;
using Njulf_Framework.Rendering.Data;

namespace Njulf_Framework.Rendering.Pipeline;

public class MeshPipeline : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private Silk.NET.Vulkan.Pipeline _pipeline;
    public PipelineLayout PipelineLayout;

    private readonly Format _colorFormat;
    private readonly Format _depthFormat;

    public Silk.NET.Vulkan.Pipeline Pipeline => _pipeline;
    public Format ColorFormat => _colorFormat;
    public Format DepthFormat => _depthFormat;

    public unsafe MeshPipeline(
        Vk vk,
        Device device,
        Extent2D swapchainExtent,
        DescriptorSetLayout[] descriptorSetLayouts,
        Format colorFormat = Format.B8G8R8A8Unorm,
        Format depthFormat = Format.D32Sfloat,
        string meshShaderPath = "Shaders/mesh.mesh",
        string fragShaderPath = "Shaders/forward_plus.frag",
        string? taskShaderPath = "Shaders/mesh.task")
    {
        _vk = vk;
        _device = device;
        _colorFormat = colorFormat;
        _depthFormat = depthFormat;

        var meshSpirv = ShaderCompiler.CompileGlslToSpirv(meshShaderPath, ShaderStage.Mesh);
        var fragSpirv = ShaderCompiler.CompileGlslToSpirv(fragShaderPath, ShaderStage.Fragment);
        byte[]? taskSpirv = null;
        if (!string.IsNullOrWhiteSpace(taskShaderPath))
        {
            taskSpirv = ShaderCompiler.CompileGlslToSpirv(taskShaderPath, ShaderStage.Task);
        }

        ShaderModule taskShaderModule = default;
        var meshShaderModule = CreateShaderModule(meshSpirv);
        var fragShaderModule = CreateShaderModule(fragSpirv);
        if (taskSpirv != null)
        {
            taskShaderModule = CreateShaderModule(taskSpirv);
        }

        try
        {
            var meshStageInfo = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.MeshBitExt,
                Module = meshShaderModule,
                PName = (byte*)System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi("main")
            };

            var fragStageInfo = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = fragShaderModule,
                PName = (byte*)System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi("main")
            };

            PipelineShaderStageCreateInfo taskStageInfo = default;
            var stageCount = 2;
            if (taskSpirv != null)
            {
                taskStageInfo = new PipelineShaderStageCreateInfo
                {
                    SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = ShaderStageFlags.TaskBitExt,
                    Module = taskShaderModule,
                    PName = (byte*)System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi("main")
                };
                stageCount = 3;
            }

            var shaderStages = stackalloc PipelineShaderStageCreateInfo[stageCount];
            var stageIndex = 0;
            if (taskSpirv != null)
            {
                shaderStages[stageIndex++] = taskStageInfo;
            }
            shaderStages[stageIndex++] = meshStageInfo;
            shaderStages[stageIndex++] = fragStageInfo;

            var viewportState = new PipelineViewportStateCreateInfo
            {
                SType = StructureType.PipelineViewportStateCreateInfo,
                ViewportCount = 1,
                PViewports = null,
                ScissorCount = 1,
                PScissors = null
            };

            var dynamicStates = stackalloc DynamicState[] { DynamicState.Viewport, DynamicState.Scissor };
            var dynamicStateInfo = new PipelineDynamicStateCreateInfo
            {
                SType = StructureType.PipelineDynamicStateCreateInfo,
                DynamicStateCount = 2,
                PDynamicStates = dynamicStates
            };

            var rasterizer = new PipelineRasterizationStateCreateInfo
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                DepthClampEnable = false,
                RasterizerDiscardEnable = false,
                PolygonMode = PolygonMode.Fill,
                LineWidth = 1.0f,
                CullMode = CullModeFlags.BackBit,
                FrontFace = FrontFace.CounterClockwise,
                DepthBiasEnable = false
            };

            var multisampling = new PipelineMultisampleStateCreateInfo
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                SampleShadingEnable = false,
                RasterizationSamples = SampleCountFlags.Count1Bit
            };

            var colorBlendAttachment = new PipelineColorBlendAttachmentState
            {
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit |
                                 ColorComponentFlags.BBit | ColorComponentFlags.ABit,
                BlendEnable = false
            };

            var colorBlending = new PipelineColorBlendStateCreateInfo
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                LogicOpEnable = false,
                LogicOp = LogicOp.Copy,
                AttachmentCount = 1,
                PAttachments = &colorBlendAttachment
            };

            var depthStencil = new PipelineDepthStencilStateCreateInfo
            {
                SType = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = true,
                DepthWriteEnable = true,
                DepthCompareOp = CompareOp.Less,
                DepthBoundsTestEnable = false,
                StencilTestEnable = false
            };

            fixed (DescriptorSetLayout* layoutsPtr = descriptorSetLayouts)
            {
                var pushConstantRange = new PushConstantRange
                {
                    StageFlags = ShaderStageFlags.MeshBitExt | ShaderStageFlags.FragmentBit |
                                 ShaderStageFlags.TaskBitExt,
                    Offset = 0,
                    Size = (uint)sizeof(Data.RenderingData.PushConstants)
                };

                var pipelineLayoutInfo = new PipelineLayoutCreateInfo
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = (uint)descriptorSetLayouts.Length,
                    PSetLayouts = layoutsPtr,
                    PushConstantRangeCount = 1,
                    PPushConstantRanges = &pushConstantRange
                };

                if (vk.CreatePipelineLayout(device, pipelineLayoutInfo, null, out PipelineLayout)
                    != Result.Success)
                {
                    throw new Exception("Failed to create mesh pipeline layout");
                }
            }

            var colorAttachmentFormat = _colorFormat;
            var depthAttachmentFormat = _depthFormat;
            var pipelineRenderingInfo = new PipelineRenderingCreateInfo
            {
                SType = StructureType.PipelineRenderingCreateInfo,
                ColorAttachmentCount = 1,
                PColorAttachmentFormats = &colorAttachmentFormat,
                DepthAttachmentFormat = depthAttachmentFormat,
                StencilAttachmentFormat = Format.Undefined
            };

            var pipelineInfo = new GraphicsPipelineCreateInfo
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                PNext = &pipelineRenderingInfo,
                StageCount = (uint)stageCount,
                PStages = shaderStages,
                PVertexInputState = null,
                PInputAssemblyState = null,
                PViewportState = &viewportState,
                PRasterizationState = &rasterizer,
                PMultisampleState = &multisampling,
                PDepthStencilState = &depthStencil,
                PColorBlendState = &colorBlending,
                PDynamicState = &dynamicStateInfo,
                Layout = PipelineLayout,
                RenderPass = default,
                Subpass = 0,
                BasePipelineHandle = default,
                BasePipelineIndex = -1
            };

            if (_vk.CreateGraphicsPipelines(_device, default, 1, &pipelineInfo, null, out var pipeline) != Result.Success)
            {
                throw new Exception("Failed to create mesh graphics pipeline");
            }

            _pipeline = pipeline;
        }
        finally
        {
            if (meshShaderModule.Handle != 0)
                _vk.DestroyShaderModule(_device, meshShaderModule, null);
            if (fragShaderModule.Handle != 0)
                _vk.DestroyShaderModule(_device, fragShaderModule, null);
            if (taskShaderModule.Handle != 0)
                _vk.DestroyShaderModule(_device, taskShaderModule, null);
        }
    }

    private unsafe ShaderModule CreateShaderModule(byte[] spirvBytecode)
    {
        fixed (byte* codePtr = spirvBytecode)
        {
            var createInfo = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)spirvBytecode.Length,
                PCode = (uint*)codePtr
            };

            if (_vk.CreateShaderModule(_device, &createInfo, null, out var shaderModule) != Result.Success)
            {
                throw new Exception("Failed to create shader module");
            }

            return shaderModule;
        }
    }

    public unsafe void Dispose()
    {
        if (_pipeline.Handle != 0)
        {
            _vk.DestroyPipeline(_device, _pipeline, null);
        }

        if (PipelineLayout.Handle != 0)
        {
            _vk.DestroyPipelineLayout(_device, PipelineLayout, null);
        }
    }
}
