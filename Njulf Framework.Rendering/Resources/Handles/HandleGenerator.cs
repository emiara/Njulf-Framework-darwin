// SPDX-License-Identifier: MPL-2.0

namespace Njulf_Framework.Rendering.Resources.Handles;

public sealed class HandleGenerator
{
    private uint _nextIndex = 1; // 0 = invalid
    private uint _generation = 1;

    public uint AllocateIndex()
    {
        // Wraparound safety: on overflow, bump generation.
        if (_nextIndex == 0)
        {
            _nextIndex = 1;
            _generation++;
        }

        return _nextIndex++;
    }

    public uint CurrentGeneration => _generation;
}