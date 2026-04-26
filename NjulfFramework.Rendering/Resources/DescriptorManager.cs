// SPDX-License-Identifier: MPL-2.0

using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace NjulfFramework.Rendering.Resources;

public class DescriptorManager : IDisposable
{
    private readonly Device _device;
    private readonly Vk _vk;
    private DescriptorPool _descriptorPool;

    private DescriptorSetLayout _descriptorSetLayout;

    public DescriptorManager(Vk vk, Device device, uint framesInFlight)
    {
        _vk = vk;
        _device = device;

        CreateDescriptorSetLayout();
        CreateDescriptorPool(framesInFlight);
        AllocateDescriptorSets(framesInFlight);
    }

    public DescriptorSetLayout DescriptorSetLayout => _descriptorSetLayout;
    public DescriptorSet[] DescriptorSets { get; private set; } = null!;


    public unsafe void Dispose()
    {
        if (_descriptorPool.Handle != 0) _vk.DestroyDescriptorPool(_device, _descriptorPool, null);

        if (_descriptorSetLayout.Handle != 0) _vk.DestroyDescriptorSetLayout(_device, _descriptorSetLayout, null);
    }

    private unsafe void CreateDescriptorSetLayout()
    {
        var uboLayoutBinding = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.VertexBit,
            PImmutableSamplers = null
        };

        var layoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = &uboLayoutBinding
        };

        if (_vk.CreateDescriptorSetLayout(_device, &layoutInfo, null, out _descriptorSetLayout) != Result.Success)
            throw new Exception("Failed to create descriptor set layout");

        Console.WriteLine("✓ Descriptor set layout created");
    }

    private unsafe void CreateDescriptorPool(uint framesInFlight)
    {
        var poolSize = new DescriptorPoolSize
        {
            Type = DescriptorType.UniformBuffer,
            DescriptorCount = framesInFlight
        };

        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            PoolSizeCount = 1,
            PPoolSizes = &poolSize,
            MaxSets = framesInFlight
        };

        if (_vk.CreateDescriptorPool(_device, &poolInfo, null, out _descriptorPool) != Result.Success)
            throw new Exception("Failed to create descriptor pool");

        Console.WriteLine("✓ Descriptor pool created");
    }

    private unsafe void AllocateDescriptorSets(uint framesInFlight)
    {
        var layouts = new DescriptorSetLayout[framesInFlight];
        Array.Fill(layouts, _descriptorSetLayout);

        fixed (DescriptorSetLayout* layoutsPtr = layouts)
        {
            var allocInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = _descriptorPool,
                DescriptorSetCount = framesInFlight,
                PSetLayouts = layoutsPtr
            };

            DescriptorSets = new DescriptorSet[framesInFlight];
            fixed (DescriptorSet* descriptorSetsPtr = DescriptorSets)
            {
                if (_vk.AllocateDescriptorSets(_device, &allocInfo, descriptorSetsPtr) != Result.Success)
                    throw new Exception("Failed to allocate descriptor sets");
            }
        }

        Console.WriteLine($"✓ Allocated {framesInFlight} descriptor sets");
    }

    public unsafe void UpdateDescriptorSet(uint frameIndex, Buffer uniformBuffer, ulong bufferSize)
    {
        if (frameIndex >= DescriptorSets.Length) throw new ArgumentOutOfRangeException(nameof(frameIndex));

        Console.WriteLine(
            $"Updating descriptor set {frameIndex} with buffer handle {uniformBuffer.Handle}, size {bufferSize}");

        var bufferInfo = new DescriptorBufferInfo
        {
            Buffer = uniformBuffer,
            Offset = 0,
            Range = bufferSize
        };

        var descriptorWrite = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = DescriptorSets[frameIndex],
            DstBinding = 0,
            DstArrayElement = 0,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            PBufferInfo = &bufferInfo,
            PImageInfo = null,
            PTexelBufferView = null
        };

        _vk.UpdateDescriptorSets(_device, 1, &descriptorWrite, 0, null);

        Console.WriteLine($"✓ Descriptor set {frameIndex} updated successfully");
    }
}