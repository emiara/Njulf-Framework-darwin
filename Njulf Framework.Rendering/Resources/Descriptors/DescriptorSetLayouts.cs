// SPDX-License-Identifier: MPL-2.0

using System;
using Silk.NET.Vulkan;

namespace Njulf_Framework.Rendering.Resources.Descriptors;

/// <summary>
/// Manages descriptor set layouts for bindless rendering (Solution 1: Single Large Binding).
/// - Set 0: Single large bindless storage buffer array (65536 descriptors)
/// - Set 1: Single large bindless texture array (65536 descriptors)
/// </summary>
public sealed class DescriptorSetLayouts : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly PhysicalDevice _physicalDevice;

    public DescriptorSetLayout BufferHeapLayout { get; private set; }
    public DescriptorSetLayout TextureHeapLayout { get; private set; }

    private const uint MaxBindlessBuffers = 65536;
    private const uint MaxBindlessTextures = 65536;

    public DescriptorSetLayouts(Vk vk, Device device, PhysicalDevice physicalDevice)
    {
        _vk = vk;
        _device = device;
        _physicalDevice = physicalDevice;
        CreateLayouts();
    }

    private unsafe void CreateLayouts()
    {
        // Query device limits for descriptor indexing
        var vk12Properties = new PhysicalDeviceVulkan12Properties
        {
            SType = StructureType.PhysicalDeviceVulkan12Properties
        };

        var properties2 = new PhysicalDeviceProperties2
        {
            SType = StructureType.PhysicalDeviceProperties2,
            PNext = &vk12Properties
        };

        _vk.GetPhysicalDeviceProperties2(_physicalDevice, &properties2);

        uint maxStorageBuffers = Math.Min(
            vk12Properties.MaxDescriptorSetUpdateAfterBindStorageBuffers,
            MaxBindlessBuffers);

        uint maxImages = Math.Min(
            vk12Properties.MaxDescriptorSetUpdateAfterBindSampledImages,
            MaxBindlessTextures);

        Console.WriteLine($"Max bindless storage buffers: {maxStorageBuffers}");
        Console.WriteLine($"Max bindless textures: {maxImages}");

        // ===== SET 0: SINGLE LARGE STORAGE BUFFER BINDING =====
        var bufferBinding = new DescriptorSetLayoutBinding
        {
            Binding = 0,  // Single binding
            DescriptorType = DescriptorType.StorageBuffer,
            DescriptorCount = maxStorageBuffers,  // 65536 descriptors
            StageFlags = ShaderStageFlags.AllGraphics | ShaderStageFlags.ComputeBit,
            PImmutableSamplers = null
        };

        var bufferBindingFlags = DescriptorBindingFlags.PartiallyBoundBit |
                                 DescriptorBindingFlags.UpdateAfterBindBit |
                                 DescriptorBindingFlags.VariableDescriptorCountBit;

        var bufferFlagsInfo = new DescriptorSetLayoutBindingFlagsCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutBindingFlagsCreateInfo,
            BindingCount = 1,
            PBindingFlags = &bufferBindingFlags
        };

        var bufferLayoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            Flags = DescriptorSetLayoutCreateFlags.UpdateAfterBindPoolBit,
            BindingCount = 1,
            PBindings = &bufferBinding,
            PNext = &bufferFlagsInfo
        };

        DescriptorSetLayout bufferLayout;
        if (_vk.CreateDescriptorSetLayout(_device, &bufferLayoutInfo, null, out bufferLayout) != Result.Success)
            throw new InvalidOperationException("Failed to create buffer heap descriptor set layout.");

        BufferHeapLayout = bufferLayout;

        // ===== SET 1: SINGLE LARGE TEXTURE BINDING =====
        var textureBinding = new DescriptorSetLayoutBinding
        {
            Binding = 0,  // Single binding
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = maxImages,  // 65536 descriptors
            StageFlags = ShaderStageFlags.AllGraphics | ShaderStageFlags.ComputeBit,
            PImmutableSamplers = null
        };

        var textureBindingFlags = DescriptorBindingFlags.PartiallyBoundBit |
                                  DescriptorBindingFlags.UpdateAfterBindBit |
                                  DescriptorBindingFlags.VariableDescriptorCountBit;

        var textureFlagsInfo = new DescriptorSetLayoutBindingFlagsCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutBindingFlagsCreateInfo,
            BindingCount = 1,
            PBindingFlags = &textureBindingFlags
        };

        var textureLayoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            Flags = DescriptorSetLayoutCreateFlags.UpdateAfterBindPoolBit,
            BindingCount = 1,
            PBindings = &textureBinding,
            PNext = &textureFlagsInfo
        };

        DescriptorSetLayout textureLayout;
        if (_vk.CreateDescriptorSetLayout(_device, &textureLayoutInfo, null, out textureLayout) != Result.Success)
            throw new InvalidOperationException("Failed to create texture heap descriptor set layout.");

        TextureHeapLayout = textureLayout;

        Console.WriteLine("✓ Descriptor layouts created (bindless single binding per set)");
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