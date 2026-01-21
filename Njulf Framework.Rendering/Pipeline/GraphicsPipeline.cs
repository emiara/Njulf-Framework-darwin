// SPDX-License-Identifier: MPL-2.0

using Silk.NET.Vulkan;
using Njulf_Framework.Rendering.Shaders;
using Njulf_Framework.Rendering.Data;
using static Silk.NET.Vulkan.Pipeline;

namespace Njulf_Framework.Rendering.Pipeline;

public class GraphicsPipeline : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private Silk.NET.Vulkan.Pipeline _pipeline;
    public PipelineLayout PipelineLayout;

    public Silk.NET.Vulkan.Pipeline Pipeline => _pipeline;

    public unsafe GraphicsPipeline(
        Vk vk,
        Device device,
        RenderPass renderPass,
        Extent2D swapchainExtent,
        DescriptorSetLayout descriptorSetLayout,
        string vertShaderPath = "Shaders/vertex.glsl",
        string fragShaderPath = "Shaders/fragment.glsl")
    {
        _vk = vk;
        _device = device;

        // Compile shaders from GLSL to SPIR-V
        Console.WriteLine($"Compiling vertex shader: {vertShaderPath}");
        var vertSpirv = ShaderCompiler.CompileGlslToSpirv(vertShaderPath, ShaderStage.Vertex);
        Console.WriteLine($"✓ Vertex shader compiled: {vertSpirv.Length} bytes");
        
        Console.WriteLine($"Compiling fragment shader: {fragShaderPath}");
        var fragSpirv = ShaderCompiler.CompileGlslToSpirv(fragShaderPath, ShaderStage.Fragment);
        Console.WriteLine($"✓ Fragment shader compiled: {fragSpirv.Length} bytes");

        try
        {
            // Create shader modules from compiled SPIR-V
            var vertShaderModule = CreateShaderModule(vertSpirv);
            var fragShaderModule = CreateShaderModule(fragSpirv);

            try
            {
                // Shader stages
                var vertStageInfo = new PipelineShaderStageCreateInfo
                {
                    SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = ShaderStageFlags.VertexBit,
                    Module = vertShaderModule,
                    PName = (byte*)System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi("main")
                };

                var fragStageInfo = new PipelineShaderStageCreateInfo
                {
                    SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = ShaderStageFlags.FragmentBit,
                    Module = fragShaderModule,
                    PName = (byte*)System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi("main")
                };

                var shaderStages = stackalloc PipelineShaderStageCreateInfo[] { vertStageInfo, fragStageInfo };

                // Vertex input state - Phase 2: Configure for actual vertex data
                // Vertex input state - with actual vertex data
                var bindingDescription = new VertexInputBindingDescription
                {
                    Binding = 0,
                    Stride = RenderingData.Vertex.GetSizeInBytes(),
                    InputRate = VertexInputRate.Vertex
                };

                var attributeDescriptions = stackalloc VertexInputAttributeDescription[3];

                // Position attribute
                attributeDescriptions[0] = new VertexInputAttributeDescription
                {
                    Binding = 0,
                    Location = 0,
                    Format = Format.R32G32B32Sfloat,
                    Offset = 0
                };

                // Normal attribute
                attributeDescriptions[1] = new VertexInputAttributeDescription
                {
                    Binding = 0,
                    Location = 1,
                    Format = Format.R32G32B32Sfloat,
                    Offset = 12
                };

                // TexCoord attribute
                attributeDescriptions[2] = new VertexInputAttributeDescription
                {
                    Binding = 0,
                    Location = 2,
                    Format = Format.R32G32Sfloat,
                    Offset = 24
                };

                var vertexInputInfo = new PipelineVertexInputStateCreateInfo
                {
                    SType = StructureType.PipelineVertexInputStateCreateInfo,
                    VertexBindingDescriptionCount = 1,
                    PVertexBindingDescriptions = &bindingDescription,
                    VertexAttributeDescriptionCount = 3,
                    PVertexAttributeDescriptions = attributeDescriptions
                };

                
                // var vertexInputInfo = new PipelineVertexInputStateCreateInfo
                // {
                //     SType = StructureType.PipelineVertexInputStateCreateInfo,
                //     VertexBindingDescriptionCount = 1,
                //     PVertexBindingDescriptions = &bindingDescription,
                //     VertexAttributeDescriptionCount = 3,
                //     PVertexAttributeDescriptions = attributeDescriptions
                // };

                // Input assembly
                var inputAssembly = new PipelineInputAssemblyStateCreateInfo
                {
                    SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                    Topology = PrimitiveTopology.TriangleList,
                    PrimitiveRestartEnable = false
                };

                // Viewport and scissor
                var viewport = new Viewport
                {
                    X = 0,
                    Y = 0,
                    Width = swapchainExtent.Width,
                    Height = swapchainExtent.Height,
                    MinDepth = 0.0f,
                    MaxDepth = 1.0f
                };

                var scissor = new Rect2D
                {
                    Offset = new Offset2D { X = 0, Y = 0 },
                    Extent = swapchainExtent
                };

                var viewportState = new PipelineViewportStateCreateInfo
                {
                    SType = StructureType.PipelineViewportStateCreateInfo,
                    ViewportCount = 1,
                    PViewports = &viewport,
                    ScissorCount = 1,
                    PScissors = &scissor
                };

                // Rasterizer
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

                // Multisampling
                var multisampling = new PipelineMultisampleStateCreateInfo
                {
                    SType = StructureType.PipelineMultisampleStateCreateInfo,
                    SampleShadingEnable = false,
                    RasterizationSamples = SampleCountFlags.Count1Bit
                };

                // Color blending
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

                colorBlending.BlendConstants[0] = 0.0f;
                colorBlending.BlendConstants[1] = 0.0f;
                colorBlending.BlendConstants[2] = 0.0f;
                colorBlending.BlendConstants[3] = 0.0f;

                // Pipeline layout (for descriptors/uniforms)
                var pipelineLayoutInfo = new PipelineLayoutCreateInfo
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = 1,
                    PSetLayouts = &descriptorSetLayout
                };

                if (_vk.CreatePipelineLayout(_device, &pipelineLayoutInfo, null, out PipelineLayout) != Result.Success)
                {
                    throw new Exception("Failed to create pipeline layout");
                }

                // Create pipeline
                var pipelineInfo = new GraphicsPipelineCreateInfo
                {
                    SType = StructureType.GraphicsPipelineCreateInfo,
                    StageCount = 2,
                    PStages = shaderStages,
                    PVertexInputState = &vertexInputInfo,
                    PInputAssemblyState = &inputAssembly,
                    PViewportState = &viewportState,
                    PRasterizationState = &rasterizer,
                    PMultisampleState = &multisampling,
                    PColorBlendState = &colorBlending,
                    Layout = PipelineLayout,
                    RenderPass = renderPass,
                    Subpass = 0,
                    BasePipelineHandle = default,
                    BasePipelineIndex = -1
                };

                if (_vk.CreateGraphicsPipelines(_device, default, 1, &pipelineInfo, null, out var pipeline) != Result.Success)
                {
                    throw new Exception("Failed to create graphics pipeline");
                }

                _pipeline = pipeline;

                Console.WriteLine("✓ Graphics pipeline created with vertex input state");
            }
            finally
            {
                // Cleanup shader modules
                _vk.DestroyShaderModule(_device, vertShaderModule, null);
                _vk.DestroyShaderModule(_device, fragShaderModule, null);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Failed to create graphics pipeline: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"  Inner exception: {ex.InnerException.Message}");
            }
            throw;
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