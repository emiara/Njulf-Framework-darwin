// SPDX-License-Identifier: MPL-2.0

using Silk.NET.Vulkan;
using System;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Njulf_Framework.Rendering.Resources.Descriptors;

/// <summary>
/// Bindless descriptor heap with single large binding per set (Solution 1).
/// Pool capacity: 65536 descriptors per type
/// </summary>
public sealed class BindlessDescriptorHeap : IDisposable
{
    private const uint MaxBindlessBuffers = 65536;
    private const uint MaxBindlessTextures = 65536;

    private readonly Vk _vk;
    private readonly Device _device;

    private DescriptorPool _bufferPool;
    private DescriptorPool _texturePool;
    private DescriptorSet _bufferSet;
    private DescriptorSet _textureSet;

    public DescriptorSet BufferSet => _bufferSet;
    public DescriptorSet TextureSet => _textureSet;

    private readonly DescriptorAllocator _bufferAllocator;
    private readonly DescriptorAllocator _textureAllocator;

    public BindlessDescriptorHeap(Vk vk, Device device, DescriptorSetLayouts layouts)
    {
        _vk = vk;
        _device = device;

        _bufferAllocator = new DescriptorAllocator(MaxBindlessBuffers);
        _textureAllocator = new DescriptorAllocator(MaxBindlessTextures);

        CreateDescriptorPools();
        AllocateDescriptorSets(layouts);
    }

    private unsafe void CreateDescriptorPools()
    {
        // Buffer pool: 65536 storage buffer descriptors
        var bufferPoolSize = new DescriptorPoolSize
        {
            Type = DescriptorType.StorageBuffer,
            DescriptorCount = MaxBindlessBuffers  // ✓ Full capacity
        };

        var bufferPoolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            Flags = DescriptorPoolCreateFlags.UpdateAfterBindBit,
            MaxSets = 1,
            PoolSizeCount = 1,
        };

        DescriptorPoolSize* bufferSizes = stackalloc DescriptorPoolSize[1];
        bufferSizes[0] = bufferPoolSize;
        bufferPoolInfo.PPoolSizes = bufferSizes;

        if (_vk.CreateDescriptorPool(_device, &bufferPoolInfo, null, out _bufferPool) != Result.Success)
            throw new InvalidOperationException("Failed to create bindless buffer descriptor pool.");

        // Texture pool: 65536 image sampler descriptors
        var texturePoolSize = new DescriptorPoolSize
        {
            Type = DescriptorType.CombinedImageSampler,
            DescriptorCount = MaxBindlessTextures  // ✓ Full capacity
        };

        var texturePoolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            Flags = DescriptorPoolCreateFlags.UpdateAfterBindBit,
            MaxSets = 1,
            PoolSizeCount = 1,
        };

        DescriptorPoolSize* textureSizes = stackalloc DescriptorPoolSize[1];
        textureSizes[0] = texturePoolSize;
        texturePoolInfo.PPoolSizes = textureSizes;

        if (_vk.CreateDescriptorPool(_device, &texturePoolInfo, null, out _texturePool) != Result.Success)
            throw new InvalidOperationException("Failed to create bindless texture descriptor pool.");
    }

    private unsafe void AllocateDescriptorSets(DescriptorSetLayouts layouts)
    {
        // Buffer set with variable descriptor count
        uint bufferDescriptorCount = MaxBindlessBuffers;
        var bufferVariableCountInfo = new DescriptorSetVariableDescriptorCountAllocateInfo
        {
            SType = StructureType.DescriptorSetVariableDescriptorCountAllocateInfo,
            DescriptorSetCount = 1,
            PDescriptorCounts = &bufferDescriptorCount
        };

        var bufferSetInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            PNext = &bufferVariableCountInfo,
            DescriptorPool = _bufferPool,
            DescriptorSetCount = 1,
        };

        DescriptorSetLayout* bufferLayouts = stackalloc DescriptorSetLayout[1];
        bufferLayouts[0] = layouts.BufferHeapLayout;
        bufferSetInfo.PSetLayouts = bufferLayouts;

        if (_vk.AllocateDescriptorSets(_device, &bufferSetInfo, out _bufferSet) != Result.Success)
            throw new InvalidOperationException("Failed to allocate bindless buffer descriptor set.");

        // Texture set with variable descriptor count
        uint textureDescriptorCount = MaxBindlessTextures;
        var textureVariableCountInfo = new DescriptorSetVariableDescriptorCountAllocateInfo
        {
            SType = StructureType.DescriptorSetVariableDescriptorCountAllocateInfo,
            DescriptorSetCount = 1,
            PDescriptorCounts = &textureDescriptorCount
        };

        var textureSetInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            PNext = &textureVariableCountInfo,
            DescriptorPool = _texturePool,
            DescriptorSetCount = 1,
        };

        DescriptorSetLayout* textureLayouts = stackalloc DescriptorSetLayout[1];
        textureLayouts[0] = layouts.TextureHeapLayout;
        textureSetInfo.PSetLayouts = textureLayouts;

        if (_vk.AllocateDescriptorSets(_device, &textureSetInfo, out _textureSet) != Result.Success)
            throw new InvalidOperationException("Failed to allocate bindless texture descriptor set.");
    }

    public bool TryAllocateBufferIndex(out uint index) =>
        _bufferAllocator.TryAllocate(out index);

    public bool TryAllocateTextureIndex(out uint index) =>
        _textureAllocator.TryAllocate(out index);

    public void FreeBufferIndex(uint index) => _bufferAllocator.Free(index);
    public void FreeTextureIndex(uint index) => _textureAllocator.Free(index);

    public unsafe void UpdateBuffer(uint index, Buffer buffer, ulong size, ulong offset = 0)
    {
        var bufferInfo = new DescriptorBufferInfo
        {
            Buffer = buffer,
            Offset = offset,
            Range = size
        };

        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _bufferSet,
            DstBinding = 0,
            DstArrayElement = index,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.StorageBuffer,
            PBufferInfo = &bufferInfo
        };

        _vk.UpdateDescriptorSets(_device, 1, &write, 0, null);
    }

    public unsafe void UpdateTexture(uint index, ImageView imageView, Sampler sampler)
    {
        var imageInfo = new DescriptorImageInfo
        {
            ImageView = imageView,
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
            Sampler = sampler
        };

        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _textureSet,
            DstBinding = 0,
            DstArrayElement = index,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            PImageInfo = &imageInfo
        };

        _vk.UpdateDescriptorSets(_device, 1, &write, 0, null);
    }

    public unsafe void Dispose()
    {
        if (_bufferPool.Handle != 0)
        {
            _vk.DestroyDescriptorPool(_device, _bufferPool, null);
            _bufferPool = default;
        }

        if (_texturePool.Handle != 0)
        {
            _vk.DestroyDescriptorPool(_device, _texturePool, null);
            _texturePool = default;
        }
    }
}