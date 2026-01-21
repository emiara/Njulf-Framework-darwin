using System.Collections.Generic;

namespace Njulf_Framework.Rendering.Resources.Descriptors;

public sealed class DescriptorAllocator
{
    private readonly Queue<uint> _freeList = new();
    private uint _next;

    public DescriptorAllocator(uint capacity)
    {
        // Pre-populate free list [0, capacity)
        for (uint i = 0; i < capacity; i++)
            _freeList.Enqueue(i);

        _next = capacity;
    }

    public bool TryAllocate(out uint index)
    {
        if (_freeList.Count > 0)
        {
            index = _freeList.Dequeue();
            return true;
        }

        // Fallback: grow linearly if needed (can cap later if you want strict limit)
        index = _next++;
        return true;
    }

    public void Free(uint index)
    {
        _freeList.Enqueue(index);
    }
}