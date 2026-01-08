using Silk.NET.Vulkan;

using Njulf_Framework.Rendering.Shaders;

namespace Njulf_Framework.Rendering.Pipeline;

public class GraphicsPipeline : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private Silk.NET.Vulkan.Pipeline _pipeline;
    private PipelineLayout _pipelineLayout;

    public Silk.NET.Vulkan.Pipeline Pipeline => _pipeline;
    public PipelineLayout PipelineLayout => _pipelineLayout;

    public GraphicsPipeline(
        Vk vk,
        Device device,
        RenderPass renderPass,
        Extent2D viewportExtent,
        string vertexShaderPath,
        string fragmentShaderPath)
    {
        _vk = vk;
        _device = device;

        CreatePipeline(renderPass, viewportExtent, vertexShaderPath, fragmentShaderPath);
    }

    private unsafe void CreatePipeline(
        RenderPass renderPass,
        Extent2D viewportExtent,
        string vertexShaderPath,
        string fragmentShaderPath)
    {
        // Load and compile shaders
        byte[] vertexSpirv = ShaderCompiler.CompileGlslToSpirv(vertexShaderPath, ShaderStage.Vertex);
        byte[] fragmentSpirv = ShaderCompiler.CompileGlslToSpirv(fragmentShaderPath, ShaderStage.Fragment);

        var vertexShaderModule = CreateShaderModule(vertexSpirv);
        var fragmentShaderModule = CreateShaderModule(fragmentSpirv);

        // Shader stages
        var vertexShaderStageInfo = new PipelineShaderStageCreateInfo
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.VertexBit,
            Module = vertexShaderModule,
            PName = (byte*)System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi("main")
        };

        var fragmentShaderStageInfo = new PipelineShaderStageCreateInfo
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.FragmentBit,
            Module = fragmentShaderModule,
            PName = (byte*)System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi("main")
        };

        var shaderStages = stackalloc[] { vertexShaderStageInfo, fragmentShaderStageInfo };

        // Vertex input (no vertex data for now - just draw fullscreen quad)
        var vertexInputInfo = new PipelineVertexInputStateCreateInfo
        {
            SType = StructureType.PipelineVertexInputStateCreateInfo,
            VertexBindingDescriptionCount = 0,
            VertexAttributeDescriptionCount = 0
        };

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
            X = 0.0f,
            Y = 0.0f,
            Width = viewportExtent.Width,
            Height = viewportExtent.Height,
            MinDepth = 0.0f,
            MaxDepth = 1.0f
        };

        var scissor = new Rect2D
        {
            Offset = new Offset2D { X = 0, Y = 0 },
            Extent = viewportExtent
        };

        var viewportState = new PipelineViewportStateCreateInfo
        {
            SType = StructureType.PipelineViewportStateCreateInfo,
            ViewportCount = 1,
            PViewports = &viewport,
            ScissorCount = 1,
            PScissors = &scissor
        };

        // Rasterization
        var rasterizer = new PipelineRasterizationStateCreateInfo
        {
            SType = StructureType.PipelineRasterizationStateCreateInfo,
            DepthClampEnable = false,
            RasterizerDiscardEnable = false,
            PolygonMode = PolygonMode.Fill,
            LineWidth = 1.0f,
            CullMode = CullModeFlags.BackBit,
            FrontFace = FrontFace.Clockwise,
            DepthBiasEnable = false
        };

        // Multisampling
        var multisampling = new PipelineMultisampleStateCreateInfo
        {
            SType = StructureType.PipelineMultisampleStateCreateInfo,
            SampleShadingEnable = false,
            RasterizationSamples = SampleCountFlags.Count1Bit,
            MinSampleShading = 1.0f
        };

        // Color blending
        var colorBlendAttachment = new PipelineColorBlendAttachmentState
        {
            ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
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

        // Pipeline layout (no descriptor sets for now)
        var pipelineLayoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 0,
            PushConstantRangeCount = 0
        };

        if (_vk.CreatePipelineLayout(_device, &pipelineLayoutInfo, null, out _pipelineLayout) != Result.Success)
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
            Layout = _pipelineLayout,
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

        // Cleanup shader modules
        _vk.DestroyShaderModule(_device, vertexShaderModule, null);
        _vk.DestroyShaderModule(_device, fragmentShaderModule, null);

        System.Runtime.InteropServices.Marshal.FreeHGlobal((IntPtr)vertexShaderStageInfo.PName);
        System.Runtime.InteropServices.Marshal.FreeHGlobal((IntPtr)fragmentShaderStageInfo.PName);
    }

    private unsafe ShaderModule CreateShaderModule(byte[] code)
    {
        var createInfo = new ShaderModuleCreateInfo
        {
            SType = StructureType.ShaderModuleCreateInfo,
            CodeSize = (uint)code.Length,
        };

        fixed (byte* codePtr = code)
        {
            createInfo.PCode = (uint*)codePtr;

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

        if (_pipelineLayout.Handle != 0)
        {
            _vk.DestroyPipelineLayout(_device, _pipelineLayout, null);
        }
    }
}