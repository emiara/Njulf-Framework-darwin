// SPDX-License-Identifier: MPL-2.0

using Silk.NET.Vulkan;
using Njulf_Framework.Rendering.Shaders;

namespace Njulf_Framework.Rendering.Pipeline;

/// <summary>
/// Compute pipeline for tiled light culling.
/// Follows the same pattern as GraphicsPipeline:
/// 1. Compile GLSL to SPIR-V using ShaderCompiler
/// 2. Create shader module from SPIR-V bytecode
/// 3. Create pipeline layout with push constants
/// 4. Create compute pipeline
/// </summary>
public class ComputePipeline : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;

    public Silk.NET.Vulkan.Pipeline Pipeline { get; private set; }
    public PipelineLayout PipelineLayout { get; private set; }

    /// <summary>
    /// Create compute pipeline from GLSL source file.
    /// Automatically compiles GLSL → SPIR-V using glslc.
    /// </summary>
    public unsafe ComputePipeline(
        Vk vk,
        Device device,
        string computeShaderPath,
        DescriptorSetLayout bindlessBufferLayout)
    {
        _vk = vk;
        _device = device;

        // Compile GLSL to SPIR-V (using ShaderCompiler like GraphicsPipeline does)
        Console.WriteLine($"Compiling compute shader: {computeShaderPath}");
        var computeSpirv = ShaderCompiler.CompileGlslToSpirv(computeShaderPath, ShaderStage.Compute);
        Console.WriteLine($"✓ Compute shader compiled: {computeSpirv.Length} bytes");

        try
        {
            // Create shader module
            var shaderModule = CreateShaderModule(computeSpirv);

            try
            {
                var setLayout = bindlessBufferLayout;
                
                // Create pipeline layout with push constants
                var pushConstantRange = new PushConstantRange
                {
                    StageFlags = ShaderStageFlags.ComputeBit,
                    Offset = 0,
                    Size = 28  // 4 uints = 16 bytes (screen width, height, light count, tile size)
                };

                var pipelineLayoutInfo = new PipelineLayoutCreateInfo
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    PushConstantRangeCount = 1,
                    PPushConstantRanges = &pushConstantRange,
                    SetLayoutCount = 1,  // Descriptor sets handled via bindless heap
                    PSetLayouts = &setLayout
                };

                if (_vk.CreatePipelineLayout(_device, &pipelineLayoutInfo, null, out var layout) != Result.Success)
                    throw new Exception("Failed to create compute pipeline layout");

                PipelineLayout = layout;
                Console.WriteLine("✓ Compute pipeline layout created");

                // Create compute pipeline
                var shaderStage = new PipelineShaderStageCreateInfo
                {
                    SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = ShaderStageFlags.ComputeBit,
                    Module = shaderModule,
                    PName = (byte*)System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi("main")
                };

                var pipelineInfo = new ComputePipelineCreateInfo
                {
                    SType = StructureType.ComputePipelineCreateInfo,
                    Stage = shaderStage,
                    Layout = PipelineLayout
                };

                if (_vk.CreateComputePipelines(_device, default, 1, &pipelineInfo, null, out var pipeline) != Result.Success)
                    throw new Exception("Failed to create compute pipeline");

                Pipeline = pipeline;
                Console.WriteLine("✓ Compute pipeline created");
            }
            finally
            {
                // Cleanup shader module (can be destroyed after pipeline creation)
                _vk.DestroyShaderModule(_device, shaderModule, null);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Failed to create compute pipeline: {ex.Message}");
            if (ex.InnerException != null)
                Console.WriteLine($"  Inner exception: {ex.InnerException.Message}");
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
                throw new Exception("Failed to create compute shader module");

            return shaderModule;
        }
    }

    public unsafe void Dispose()
    {
        if (Pipeline.Handle != 0)
            _vk.DestroyPipeline(_device, Pipeline, null);

        if (PipelineLayout.Handle != 0)
            _vk.DestroyPipelineLayout(_device, PipelineLayout, null);
    }
}