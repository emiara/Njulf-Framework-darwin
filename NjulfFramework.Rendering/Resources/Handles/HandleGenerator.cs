// SPDX-License-Identifier: MPL-2.0

namespace NjulfFramework.Rendering.Resources.Handles;

public sealed class HandleGenerator
{
    private uint _nextIndex = 1; // 0 = invalid

    public uint CurrentGeneration { get; private set; } = 1;

    public uint AllocateIndex()
    {
        // Wraparound safety: on overflow, bump generation.
        if (_nextIndex == 0)
        {
            _nextIndex = 1;
            CurrentGeneration++;
        }

        return _nextIndex++;
    }
}