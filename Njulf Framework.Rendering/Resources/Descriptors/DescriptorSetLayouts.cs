// SPDX-License-Identifier: MPL-2.0

using System;
using Silk.NET.Vulkan;

namespace Njulf_Framework.Rendering.Resources.Descriptors;

public sealed class DescriptorSetLayouts : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;

    public DescriptorSetLayout BufferHeapLayout { get; private set; }
    public DescriptorSetLayout TextureHeapLayout { get; private set; }

    public DescriptorSetLayouts(Vk vk, Device device)
    {
        _vk = vk;
        _device = device;

        CreateLayouts();
    }

    private unsafe void CreateLayouts()
    {
        // Bindless storage buffer array: set = 0, binding = 0
        var bufferBinding = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.StorageBuffer,
            DescriptorCount = uint.MaxValue, // will be overridden via flags; effectively unbounded
            StageFlags = ShaderStageFlags.AllGraphics,
        };

        // Bindless combined image sampler array: set = 1, binding = 0
        var textureBinding = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = uint.MaxValue,
            StageFlags = ShaderStageFlags.AllGraphics,
        };

        // Descriptor indexing flags so arrays can be partially bound & variable-sized.
        var bufferBindingFlags = DescriptorBindingFlags.PartiallyBoundBit |
                                 DescriptorBindingFlags.UpdateAfterBindBit |
                                 DescriptorBindingFlags.VariableDescriptorCountBit;

        var textureBindingFlags = DescriptorBindingFlags.PartiallyBoundBit |
                                  DescriptorBindingFlags.UpdateAfterBindBit |
                                  DescriptorBindingFlags.VariableDescriptorCountBit;

        var bufferFlagsInfo = new DescriptorSetLayoutBindingFlagsCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutBindingFlagsCreateInfo,
            BindingCount = 1,
        };

        var textureFlagsInfo = new DescriptorSetLayoutBindingFlagsCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutBindingFlagsCreateInfo,
            BindingCount = 1,
        };

        // Allocate unmanaged arrays for flags
        DescriptorBindingFlags* bufferFlags = stackalloc DescriptorBindingFlags[1];
        bufferFlags[0] = bufferBindingFlags;
        bufferFlagsInfo.PBindingFlags = bufferFlags;

        DescriptorBindingFlags* textureFlags = stackalloc DescriptorBindingFlags[1];
        textureFlags[0] = textureBindingFlags;
        textureFlagsInfo.PBindingFlags = textureFlags;

        // Buffer layout
        var bufferLayoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            Flags = DescriptorSetLayoutCreateFlags.UpdateAfterBindPoolBit,
            BindingCount = 1,
        };

        DescriptorSetLayoutBinding* bufferBindingsPtr = stackalloc DescriptorSetLayoutBinding[1];
        bufferBindingsPtr[0] = bufferBinding;
        bufferLayoutInfo.PBindings = bufferBindingsPtr;
        bufferLayoutInfo.PNext = &bufferFlagsInfo;
        
        DescriptorSetLayout bufferLayout;
        if (_vk.CreateDescriptorSetLayout(_device, &bufferLayoutInfo, null, out bufferLayout) != Result.Success)
        {
            throw new InvalidOperationException("Failed to create buffer heap descriptor set layout.");
        }
        BufferHeapLayout = bufferLayout;

        // Texture layout
        var textureLayoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            Flags = DescriptorSetLayoutCreateFlags.UpdateAfterBindPoolBit,
            BindingCount = 1,
        };

        DescriptorSetLayoutBinding* textureBindingsPtr = stackalloc DescriptorSetLayoutBinding[1];
        textureBindingsPtr[0] = textureBinding;
        textureLayoutInfo.PBindings = textureBindingsPtr;
        textureLayoutInfo.PNext = &textureFlagsInfo;
    
        DescriptorSetLayout textureLayout;
        if (_vk.CreateDescriptorSetLayout(_device, &textureLayoutInfo, null, out textureLayout) != Result.Success)
        {
            throw new InvalidOperationException("Failed to create texture heap descriptor set layout.");
        }
        TextureHeapLayout = textureLayout;
    }

    public unsafe void Dispose()
    {
        if (BufferHeapLayout.Handle != 0)
        {
            _vk.DestroyDescriptorSetLayout(_device, BufferHeapLayout, null);
            BufferHeapLayout = default;
        }

        if (TextureHeapLayout.Handle != 0)
        {
            _vk.DestroyDescriptorSetLayout(_device, TextureHeapLayout, null);
            TextureHeapLayout = default;
        }
    }
}